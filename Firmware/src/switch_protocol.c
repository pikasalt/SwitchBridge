/**
 * SwitchBridge Firmware - Nintendo Switch Protocol Handler
 *
 * Implements the Pro Controller's Bluetooth HID protocol:
 * - Builds 0x30 standard input reports with IMU data
 * - Handles 0x01 output reports (subcommands from the Switch)
 * - Fakes SPI flash reads for calibration data
 *
 * Protocol reference: dekuNukem/Nintendo_Switch_Reverse_Engineering
 */

#include <string.h>
#include "pico/stdlib.h"
#include "switchbridge.h"

// ============================================================================
// Internal state
// ============================================================================

static uint8_t timer_counter = 0;
static bool imu_enabled = false;
static bool vibration_enabled = false;
static uint8_t player_leds = 0;

// Default SPI flash contents (faked for the Switch)
// The Switch reads various calibration and device info from SPI flash
// during the pairing handshake.

// Serial number at 0x6000 (16 bytes)
static const uint8_t spi_serial[] = {
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
};

// Device info area
static const uint8_t spi_device_info[] = {
    0x03, 0x48,  // FW version
    0x03,        // Controller type (Pro Controller)
    0x02,        // Unknown
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00  // Padding
};

// Body/button colors at 0x6050
static const uint8_t spi_colors[] = {
    0x32, 0x32, 0x32,  // Body (dark gray)
    0xFF, 0xFF, 0xFF,  // Buttons (white)
    0x32, 0x32, 0x32,  // Left grip
    0x32, 0x32, 0x32   // Right grip
};

// Factory stick calibration at 0x603D (9 bytes left + 9 bytes right)
static const uint8_t spi_stick_cal[] = {
    // Left stick cal (max above, center, min below) - 3 bytes each
    0xFF, 0xF7, 0x7F,  // max
    0x00, 0x08, 0x80,  // center
    0xFF, 0xF7, 0x7F,  // min
    // Right stick cal
    0x00, 0x08, 0x80,  // center
    0xFF, 0xF7, 0x7F,  // max
    0xFF, 0xF7, 0x7F,  // min
    // Deadzone and range (extra bytes the Switch may read)
    0x0F, 0x0F, 0x03, 0x03, 0x00, 0x00, 0x00, 0x00,
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
};

// Factory IMU calibration at 0x6020
static const uint8_t spi_imu_cal[] = {
    // Accel offsets (x, y, z) int16 LE
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    // Accel sensitivity coeff
    0x00, 0x40, 0x00, 0x40, 0x00, 0x40,
    // Gyro offsets
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    // Gyro sensitivity coeff
    0x3B, 0x34, 0x3B, 0x34, 0x3B, 0x34
};

// User calibration check bytes at 0x8010 and 0x8026
// Magic bytes 0xB2A1 = calibration exists, otherwise use factory
static const uint8_t spi_user_cal_sticks[] = {
    0xFF, 0xFF,  // No user calibration (not 0xB2, 0xA1)
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    0x00, 0x00, 0x00, 0x00
};

static const uint8_t spi_user_cal_imu[] = {
    0xFF, 0xFF,  // No user calibration
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
};

// ============================================================================
// Helper: encode stick data in Pro Controller's 12-bit packed format
// ============================================================================

// Pro Controller sticks use 12-bit values packed into 3 bytes per axis pair.
// Format for a stick: [low_x | (high_x << 8 & 0xF00)] [(high_x >> 4) | (low_y << 4)] [high_y]
// Input: 0-4095 range values
static void encode_stick(uint8_t *out, uint16_t x, uint16_t y) {
    out[0] = (uint8_t)(x & 0xFF);
    out[1] = (uint8_t)(((x >> 8) & 0x0F) | ((y & 0x0F) << 4));
    out[2] = (uint8_t)((y >> 4) & 0xFF);
}

// ============================================================================
// Build a standard 0x30 input report with IMU data
// ============================================================================

void switch_protocol_build_input_report(uint8_t *report, const controller_state_t *state) {
    memset(report, 0, 49);

    // Byte 0: Report ID
    report[0] = REPORT_ID_INPUT_FULL;

    // Byte 1: Timer (wrapping counter, increments per report)
    report[1] = timer_counter++;

    // Byte 2: Battery + connection info
    // Battery: 8 = full, Connection: Pro Controller powered by Switch
    report[2] = (0x08 << 4) | 0x0E;  // Full battery, Pro Controller, powered

    // Bytes 3-5: Button state
    // Byte 3: Right-side buttons (Y, X, B, A, SR, SL, R, ZR)
    report[3] = 0;
    if (state->btn_y)  report[3] |= 0x01;
    if (state->btn_x)  report[3] |= 0x02;
    if (state->btn_b)  report[3] |= 0x04;
    if (state->btn_a)  report[3] |= 0x08;
    if (state->btn_r)  report[3] |= 0x40;
    if (state->btn_zr) report[3] |= 0x80;

    // Byte 4: Shared buttons (Minus, Plus, RStick, LStick, Home, Capture)
    report[4] = 0;
    if (state->btn_minus)  report[4] |= 0x01;
    if (state->btn_plus)   report[4] |= 0x02;
    if (state->btn_rstick) report[4] |= 0x04;
    if (state->btn_lstick) report[4] |= 0x08;
    if (state->btn_home)   report[4] |= 0x10;
    if (state->btn_capture) report[4] |= 0x20;

    // Byte 5: D-Pad + left-side buttons
    report[5] = 0;
    if (state->dpad_down)  report[5] |= 0x01;
    if (state->dpad_up)    report[5] |= 0x02;
    if (state->dpad_right) report[5] |= 0x04;
    if (state->dpad_left)  report[5] |= 0x08;
    if (state->btn_l)      report[5] |= 0x40;
    if (state->btn_zl)     report[5] |= 0x80;

    // Bytes 6-8: Left stick (12-bit packed)
    encode_stick(&report[6], state->lx, state->ly);

    // Bytes 9-11: Right stick (12-bit packed)
    encode_stick(&report[9], state->rx, state->ry);

    // Byte 12: Vibrator input report (not used, keep 0)
    report[12] = 0x00;

    // Bytes 13-48: IMU data (3 samples, 12 bytes each = 36 bytes)
    // Each sample: accel_x, accel_y, accel_z, gyro_x, gyro_y, gyro_z (int16 LE each)
    if (imu_enabled) {
        for (int sample = 0; sample < 3; sample++) {
            int offset = 13 + (sample * 12);

            // Accelerometer (int16 LE)
            report[offset + 0] = (uint8_t)(state->accel_x & 0xFF);
            report[offset + 1] = (uint8_t)((state->accel_x >> 8) & 0xFF);
            report[offset + 2] = (uint8_t)(state->accel_y & 0xFF);
            report[offset + 3] = (uint8_t)((state->accel_y >> 8) & 0xFF);
            report[offset + 4] = (uint8_t)(state->accel_z & 0xFF);
            report[offset + 5] = (uint8_t)((state->accel_z >> 8) & 0xFF);

            // Gyroscope (int16 LE)
            report[offset + 6] = (uint8_t)(state->gyro_x & 0xFF);
            report[offset + 7] = (uint8_t)((state->gyro_x >> 8) & 0xFF);
            report[offset + 8] = (uint8_t)(state->gyro_y & 0xFF);
            report[offset + 9] = (uint8_t)((state->gyro_y >> 8) & 0xFF);
            report[offset + 10] = (uint8_t)(state->gyro_z & 0xFF);
            report[offset + 11] = (uint8_t)((state->gyro_z >> 8) & 0xFF);
        }
    }
}

// ============================================================================
// Handle SPI flash read requests
// ============================================================================

static void handle_spi_read(uint8_t *reply_data, uint32_t addr, uint8_t len) {
    // Zero-fill by default
    memset(reply_data, 0, len);

    // Serve known SPI regions
    if (addr == 0x6000 && len <= sizeof(spi_serial)) {
        memcpy(reply_data, spi_serial, len);
    }
    else if (addr >= 0x6012 && addr < 0x6012 + sizeof(spi_device_info)) {
        uint32_t off = addr - 0x6012;
        uint8_t copylen = (len < sizeof(spi_device_info) - off) ? len : sizeof(spi_device_info) - off;
        memcpy(reply_data, spi_device_info + off, copylen);
    }
    else if (addr >= 0x6020 && addr < 0x6020 + sizeof(spi_imu_cal)) {
        uint32_t off = addr - 0x6020;
        uint8_t copylen = (len < sizeof(spi_imu_cal) - off) ? len : sizeof(spi_imu_cal) - off;
        memcpy(reply_data, spi_imu_cal + off, copylen);
    }
    else if (addr >= 0x603D && addr < 0x603D + sizeof(spi_stick_cal)) {
        uint32_t off = addr - 0x603D;
        uint8_t copylen = (len < sizeof(spi_stick_cal) - off) ? len : sizeof(spi_stick_cal) - off;
        memcpy(reply_data, spi_stick_cal + off, copylen);
    }
    else if (addr >= 0x6050 && addr < 0x6050 + sizeof(spi_colors)) {
        uint32_t off = addr - 0x6050;
        uint8_t copylen = (len < sizeof(spi_colors) - off) ? len : sizeof(spi_colors) - off;
        memcpy(reply_data, spi_colors + off, copylen);
    }
    else if (addr >= 0x8010 && addr < 0x8010 + sizeof(spi_user_cal_sticks)) {
        uint32_t off = addr - 0x8010;
        uint8_t copylen = (len < sizeof(spi_user_cal_sticks) - off) ? len : sizeof(spi_user_cal_sticks) - off;
        memcpy(reply_data, spi_user_cal_sticks + off, copylen);
    }
    else if (addr >= 0x8026 && addr < 0x8026 + sizeof(spi_user_cal_imu)) {
        uint32_t off = addr - 0x8026;
        uint8_t copylen = (len < sizeof(spi_user_cal_imu) - off) ? len : sizeof(spi_user_cal_imu) - off;
        memcpy(reply_data, spi_user_cal_imu + off, copylen);
    }
}

// ============================================================================
// Build a subcommand reply (report ID 0x21)
// ============================================================================

void switch_protocol_build_subcmd_reply(uint8_t *report, uint8_t subcmd_id,
                                         const uint8_t *subcmd_data, uint8_t subcmd_len,
                                         const controller_state_t *state) {
    memset(report, 0, 49);

    report[0] = REPORT_ID_SUBCMD_REPLY;
    report[1] = timer_counter++;

    // Battery + connection info
    report[2] = (0x08 << 4) | 0x0E;

    // Button state (can be current state)
    // Bytes 3-5: buttons, 6-8: left stick, 9-11: right stick
    // (simplified: just zeros for subcommand replies)

    // Byte 13: ACK byte
    report[13] = 0x80;  // ACK

    // Byte 14: Subcommand ID being replied to
    report[14] = subcmd_id;

    // Bytes 15+: Subcommand reply data
    if (subcmd_data && subcmd_len > 0) {
        memcpy(&report[15], subcmd_data, subcmd_len < 34 ? subcmd_len : 34);
    }
}

// ============================================================================
// Handle output reports from the Switch (subcommands)
// ============================================================================

void switch_protocol_handle_output(const uint8_t *data, uint16_t len) {
    if (len < 2) return;

    uint8_t report_id = data[0];

    if (report_id == REPORT_ID_RUMBLE_ONLY) {
        // 0x10: Rumble only - we don't have a motor, just ignore
        return;
    }

    if (report_id != REPORT_ID_OUTPUT || len < 12) {
        return;
    }

    // data[1..8]: rumble data (ignored)
    // data[10]: subcommand ID
    uint8_t subcmd = data[10];

    uint8_t reply[49];
    uint8_t reply_data[35];
    memset(reply_data, 0, sizeof(reply_data));
    controller_state_t dummy = {0};
    dummy.lx = 2048;
    dummy.ly = 2048;
    dummy.rx = 2048;
    dummy.ry = 2048;

    switch (subcmd) {
        case SUBCMD_REQUEST_INFO: {
            // Reply with firmware version, controller type, MAC
            reply_data[0] = 0x03;  // FW version major
            reply_data[1] = 0x48;  // FW version minor
            reply_data[2] = CONTROLLER_TYPE_PROCON;
            reply_data[3] = 0x02;  // Unknown
            // Bytes 4-9: MAC address (use BT MAC or dummy)
            reply_data[4] = 0xAA;
            reply_data[5] = 0xBB;
            reply_data[6] = 0xCC;
            reply_data[7] = 0xDD;
            reply_data[8] = 0xEE;
            reply_data[9] = 0xFF;
            reply_data[10] = 0x03; // Unknown
            reply_data[11] = 0x01; // Using colors from SPI

            switch_protocol_build_subcmd_reply(reply, subcmd, reply_data, 12, &dummy);
            break;
        }

        case SUBCMD_SET_INPUT_MODE: {
            // data[11]: mode (0x30 = standard full, 0x3F = simple HID)
            // ACK it
            switch_protocol_build_subcmd_reply(reply, subcmd, NULL, 0, &dummy);
            break;
        }

        case SUBCMD_TRIGGER_ELAPSED: {
            switch_protocol_build_subcmd_reply(reply, subcmd, NULL, 0, &dummy);
            break;
        }

        case SUBCMD_ENABLE_IMU: {
            imu_enabled = (len > 11 && data[11] == 0x01);
            switch_protocol_build_subcmd_reply(reply, subcmd, NULL, 0, &dummy);
            break;
        }

        case SUBCMD_SET_IMU_SENS: {
            // ACK - we accept any sensitivity setting
            switch_protocol_build_subcmd_reply(reply, subcmd, NULL, 0, &dummy);
            break;
        }

        case SUBCMD_ENABLE_VIBRATION: {
            vibration_enabled = (len > 11 && data[11] == 0x01);
            switch_protocol_build_subcmd_reply(reply, subcmd, NULL, 0, &dummy);
            break;
        }

        case SUBCMD_SET_PLAYER_LED: {
            if (len > 11) {
                player_leds = data[11];
            }
            switch_protocol_build_subcmd_reply(reply, subcmd, NULL, 0, &dummy);
            break;
        }

        case SUBCMD_SET_HOME_LED: {
            // ACK (we don't have a home LED)
            switch_protocol_build_subcmd_reply(reply, subcmd, NULL, 0, &dummy);
            break;
        }

        case SUBCMD_SPI_READ: {
            if (len < 16) break;

            // data[11-14]: address (uint32 LE)
            uint32_t addr = data[11] | (data[12] << 8) |
                           (data[13] << 16) | (data[14] << 24);
            uint8_t read_len = data[15];
            if (read_len > 29) read_len = 29;  // Max reply data

            // Reply format: address (4 bytes) + length (1 byte) + data
            reply_data[0] = data[11];
            reply_data[1] = data[12];
            reply_data[2] = data[13];
            reply_data[3] = data[14];
            reply_data[4] = read_len;

            handle_spi_read(&reply_data[5], addr, read_len);

            switch_protocol_build_subcmd_reply(reply, subcmd, reply_data, 5 + read_len, &dummy);
            break;
        }

        case SUBCMD_SET_NFC_IR_MCU:
        case SUBCMD_SET_NFC_IR_STATE: {
            // ACK these even though we don't support NFC/IR
            switch_protocol_build_subcmd_reply(reply, subcmd, NULL, 0, &dummy);
            break;
        }

        default: {
            // Unknown subcommand - send generic ACK
            switch_protocol_build_subcmd_reply(reply, subcmd, NULL, 0, &dummy);
            break;
        }
    }

    // Send the reply (via bt_hid)
    // Note: In a full implementation, this would queue the reply to be sent
    // via the HID interrupt channel. For now, we rely on the bt_hid layer.
    extern void bt_hid_send_raw_report(const uint8_t *data, uint16_t len);
    bt_hid_send_raw_report(reply, sizeof(reply));
}
