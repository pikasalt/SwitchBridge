/**
 * SwitchBridge Firmware - Serial Input Handler
 *
 * Reads binary controller state packets from the host PC over USB CDC serial.
 * Packet format (26 bytes):
 *   [0xAA] [0x55] [buttons_lo] [buttons_hi] [hat/misc]
 *   [lx_lo] [lx_hi] [ly_lo] [ly_hi]
 *   [rx_lo] [rx_hi] [ry_lo] [ry_hi]
 *   [accel_x_lo] [accel_x_hi] [accel_y_lo] [accel_y_hi] [accel_z_lo] [accel_z_hi]
 *   [gyro_x_lo] [gyro_x_hi] [gyro_y_lo] [gyro_y_hi] [gyro_z_lo] [gyro_z_hi]
 *   [checksum]
 */

#include <string.h>
#include "pico/stdlib.h"
#include "tusb.h"
#include "switchbridge.h"

// ============================================================================
// Ring buffer for incoming serial data
// ============================================================================

#define RING_BUF_SIZE 256

static uint8_t ring_buf[RING_BUF_SIZE];
static volatile uint16_t ring_head = 0;
static volatile uint16_t ring_tail = 0;

static inline uint16_t ring_available(void) {
    return (ring_head - ring_tail) & (RING_BUF_SIZE - 1);
}

static inline uint8_t ring_read_byte(void) {
    uint8_t val = ring_buf[ring_tail];
    ring_tail = (ring_tail + 1) & (RING_BUF_SIZE - 1);
    return val;
}

static inline uint8_t ring_peek(uint16_t offset) {
    return ring_buf[(ring_tail + offset) & (RING_BUF_SIZE - 1)];
}

static inline void ring_skip(uint16_t count) {
    ring_tail = (ring_tail + count) & (RING_BUF_SIZE - 1);
}

// ============================================================================
// Initialization
// ============================================================================

void serial_input_init(void) {
    // TinyUSB CDC initialization is handled by tusb_init() which should be
    // called during board init. The CDC device is configured in tusb_config.h.
    tusb_init();
}

// ============================================================================
// Read and parse a controller state packet
// ============================================================================

// Pull any available data from USB CDC into our ring buffer
static void poll_cdc(void) {
    tud_task();  // TinyUSB device task

    if (!tud_cdc_connected() || !tud_cdc_available()) return;

    while (tud_cdc_available()) {
        uint8_t byte;
        if (tud_cdc_read(&byte, 1) == 1) {
            uint16_t next_head = (ring_head + 1) & (RING_BUF_SIZE - 1);
            if (next_head != ring_tail) {  // Don't overflow
                ring_buf[ring_head] = byte;
                ring_head = next_head;
            }
        }
    }
}

// Map int16 stick value (-32768..32767) to Pro Controller 12-bit (0..4095)
static uint16_t map_stick(int16_t value) {
    // Input: -32768 to 32767
    // Output: 0 to 4095 (center = 2048)
    int32_t mapped = ((int32_t)value + 32768) * 4095 / 65535;
    if (mapped < 0) mapped = 0;
    if (mapped > 4095) mapped = 4095;
    return (uint16_t)mapped;
}

bool serial_input_read(controller_state_t *state) {
    // Pull data from USB
    poll_cdc();

    // Need at least a full packet
    if (ring_available() < SERIAL_PACKET_SIZE) return false;

    // Scan for sync markers
    while (ring_available() >= SERIAL_PACKET_SIZE) {
        if (ring_peek(0) == SERIAL_SYNC_0 && ring_peek(1) == SERIAL_SYNC_1) {
            break;  // Found sync
        }
        ring_skip(1);  // Skip garbage byte
    }

    if (ring_available() < SERIAL_PACKET_SIZE) return false;

    // Read the packet
    uint8_t pkt[SERIAL_PACKET_SIZE];
    for (int i = 0; i < SERIAL_PACKET_SIZE; i++) {
        pkt[i] = ring_peek(i);
    }

    // Verify checksum (XOR of bytes 2-24)
    uint8_t checksum = 0;
    for (int i = 2; i < SERIAL_PACKET_SIZE - 1; i++) {
        checksum ^= pkt[i];
    }
    if (checksum != pkt[SERIAL_PACKET_SIZE - 1]) {
        // Bad checksum - skip sync bytes and try again
        ring_skip(2);
        return false;
    }

    // Consume the packet
    ring_skip(SERIAL_PACKET_SIZE);

    // Parse button state
    uint8_t btn_lo = pkt[2];
    uint8_t btn_hi = pkt[3];
    uint8_t btn_misc = pkt[4];

    state->btn_y       = (btn_lo >> 0) & 1;
    state->btn_x       = (btn_lo >> 1) & 1;
    state->btn_b       = (btn_lo >> 2) & 1;
    state->btn_a       = (btn_lo >> 3) & 1;
    state->btn_r       = (btn_lo >> 6) & 1;
    state->btn_zr      = (btn_lo >> 7) & 1;

    state->btn_minus   = (btn_hi >> 0) & 1;
    state->btn_plus    = (btn_hi >> 1) & 1;
    state->btn_rstick  = (btn_hi >> 2) & 1;
    state->btn_lstick  = (btn_hi >> 3) & 1;
    state->btn_home    = (btn_hi >> 4) & 1;
    state->btn_capture = (btn_hi >> 5) & 1;

    state->dpad_down   = (btn_misc >> 0) & 1;
    state->dpad_up     = (btn_misc >> 1) & 1;
    state->dpad_right  = (btn_misc >> 2) & 1;
    state->dpad_left   = (btn_misc >> 3) & 1;
    state->btn_l       = (btn_misc >> 6) & 1;
    state->btn_zl      = (btn_misc >> 7) & 1;

    // Parse sticks (int16 LE -> 12-bit Pro Controller range)
    int16_t lx_raw = (int16_t)(pkt[5] | (pkt[6] << 8));
    int16_t ly_raw = (int16_t)(pkt[7] | (pkt[8] << 8));
    int16_t rx_raw = (int16_t)(pkt[9] | (pkt[10] << 8));
    int16_t ry_raw = (int16_t)(pkt[11] | (pkt[12] << 8));

    state->lx = map_stick(lx_raw);
    state->ly = map_stick(ly_raw);
    state->rx = map_stick(rx_raw);
    state->ry = map_stick(ry_raw);

    // Parse IMU data (passed through as raw int16)
    state->accel_x = (int16_t)(pkt[13] | (pkt[14] << 8));
    state->accel_y = (int16_t)(pkt[15] | (pkt[16] << 8));
    state->accel_z = (int16_t)(pkt[17] | (pkt[18] << 8));
    state->gyro_x  = (int16_t)(pkt[19] | (pkt[20] << 8));
    state->gyro_y  = (int16_t)(pkt[21] | (pkt[22] << 8));
    state->gyro_z  = (int16_t)(pkt[23] | (pkt[24] << 8));

    return true;
}
