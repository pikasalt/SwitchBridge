#ifndef SWITCHBRIDGE_H
#define SWITCHBRIDGE_H

#include <stdint.h>
#include <stdbool.h>

// ============================================================================
// Serial protocol constants (PC -> Pico)
// ============================================================================

#define SERIAL_SYNC_0       0xAA
#define SERIAL_SYNC_1       0x55
#define SERIAL_PACKET_SIZE  26  // 2 sync + 23 data + 1 checksum

// ============================================================================
// Nintendo Switch Pro Controller constants
// ============================================================================

// Bluetooth HID report IDs
#define REPORT_ID_INPUT_FULL    0x30    // Standard full mode with IMU
#define REPORT_ID_INPUT_SIMPLE  0x3F    // Simple HID mode
#define REPORT_ID_SUBCMD_REPLY  0x21    // Subcommand reply
#define REPORT_ID_OUTPUT        0x01    // Output report (subcmds from Switch)
#define REPORT_ID_RUMBLE_ONLY   0x10    // Rumble only output

// Subcommand IDs (from Switch)
#define SUBCMD_REQUEST_INFO     0x02
#define SUBCMD_SET_INPUT_MODE   0x03
#define SUBCMD_TRIGGER_ELAPSED  0x04
#define SUBCMD_GET_PAGE_LIST    0x05
#define SUBCMD_SET_HCI_STATE    0x06
#define SUBCMD_RESET_PAIRING    0x07
#define SUBCMD_SET_SHIPMODE     0x08
#define SUBCMD_SPI_READ         0x10
#define SUBCMD_SPI_WRITE        0x11
#define SUBCMD_SET_NFC_IR_MCU   0x22
#define SUBCMD_SET_NFC_IR_STATE 0x23
#define SUBCMD_SET_PLAYER_LED   0x30
#define SUBCMD_GET_PLAYER_LED   0x31
#define SUBCMD_SET_HOME_LED     0x38
#define SUBCMD_ENABLE_IMU       0x40
#define SUBCMD_SET_IMU_SENS     0x41
#define SUBCMD_WRITE_IMU_REG    0x42
#define SUBCMD_READ_IMU_REG     0x43
#define SUBCMD_ENABLE_VIBRATION 0x48
#define SUBCMD_GET_VOLTAGE      0x50

// Controller type
#define CONTROLLER_TYPE_PROCON  0x03

// SPI flash addresses
#define SPI_SERIAL_NUMBER       0x6000
#define SPI_DEVICE_INFO         0x6012
#define SPI_COLOR_DATA          0x6050
#define SPI_FACTORY_PARAMS      0x6080
#define SPI_USER_CAL_STICKS     0x8010
#define SPI_FACTORY_CAL_STICKS  0x603D
#define SPI_FACTORY_CAL_IMU     0x6020
#define SPI_USER_CAL_IMU        0x8026
#define SPI_IMU_HORIZONTAL_OFF  0x6080

// ============================================================================
// Input state structures
// ============================================================================

typedef struct {
    // Buttons (mapped to Pro Controller layout)
    bool btn_y, btn_x, btn_b, btn_a;
    bool btn_r, btn_zr;
    bool btn_l, btn_zl;
    bool btn_minus, btn_plus;
    bool btn_lstick, btn_rstick;
    bool btn_home, btn_capture;
    bool dpad_up, dpad_down, dpad_left, dpad_right;

    // Analog sticks (0-4095 range, center ~2048)
    uint16_t lx, ly;
    uint16_t rx, ry;

    // IMU data (raw int16 values, LSM6DS3 format)
    int16_t accel_x, accel_y, accel_z;
    int16_t gyro_x, gyro_y, gyro_z;
} controller_state_t;

// ============================================================================
// Function declarations
// ============================================================================

// bt_hid.c
void bt_hid_init(void);
void bt_hid_send_input_report(const controller_state_t *state);
bool bt_hid_is_connected(void);

// serial_input.c
void serial_input_init(void);
bool serial_input_read(controller_state_t *state);

// switch_protocol.c
void switch_protocol_handle_output(const uint8_t *data, uint16_t len);
void switch_protocol_build_input_report(uint8_t *report, const controller_state_t *state);
void switch_protocol_build_subcmd_reply(uint8_t *report, uint8_t subcmd_id,
                                         const uint8_t *subcmd_data, uint8_t subcmd_len,
                                         const controller_state_t *state);

#endif // SWITCHBRIDGE_H
