/**
 * SwitchBridge Firmware - Bluetooth HID (Pro Controller Emulation)
 *
 * Implements a Bluetooth HID device that appears as an authentic Nintendo
 * Switch Pro Controller. Handles pairing, subcommand responses, and sends
 * standard 0x30 input reports with IMU data.
 *
 * Based on reverse engineering from:
 * - dekuNukem/Nintendo_Switch_Reverse_Engineering
 * - Poohl/joycontrol
 * - DavidPagels/retro-pico-switch
 */

#include <string.h>
#include "pico/stdlib.h"
#include "pico/cyw43_arch.h"

// BTStack headers
#include "btstack.h"
#include "btstack_run_loop.h"
#include "classic/sdp_server.h"
#include "classic/hid_device.h"

#include "switchbridge.h"

// ============================================================================
// Pro Controller HID descriptor
// ============================================================================

// This is a simplified HID descriptor that the Switch expects from a
// Pro Controller. The Switch largely ignores the descriptor and uses its
// own protocol, but it must be present for the initial connection.
static const uint8_t hid_descriptor[] = {
    0x05, 0x01,       // Usage Page (Generic Desktop)
    0x09, 0x05,       // Usage (Game Pad)
    0xA1, 0x01,       // Collection (Application)
    0x06, 0x01, 0xFF, //   Usage Page (Vendor Defined)
    0x09, 0x21,       //   Usage (Vendor Usage)
    0x15, 0x00,       //   Logical Minimum (0)
    0x26, 0xFF, 0x00, //   Logical Maximum (255)
    0x75, 0x08,       //   Report Size (8)
    0x95, 0x30,       //   Report Count (48)
    0x81, 0x02,       //   Input (Data, Var, Abs)
    0x06, 0x01, 0xFF, //   Usage Page (Vendor Defined)
    0x09, 0x21,       //   Usage (Vendor Usage)
    0x15, 0x00,       //   Logical Minimum (0)
    0x26, 0xFF, 0x00, //   Logical Maximum (255)
    0x75, 0x08,       //   Report Size (8)
    0x95, 0x30,       //   Report Count (48)
    0x91, 0x02,       //   Output (Data, Var, Abs)
    0xC0              // End Collection
};

// ============================================================================
// State
// ============================================================================

static bool connected = false;
static uint16_t hid_cid = 0;
static uint8_t report_timer = 0;

// Fake SPI flash data for the Switch's queries
// These are default calibration/info values that the Switch expects

// Device info at 0x6012
static const uint8_t device_info[] = {
    0x03,       // Controller type: Pro Controller
    0x02,       // Unknown
    // MAC address (dummy)
    0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF
};

// Controller colors at 0x6050
static const uint8_t controller_colors[] = {
    0x32, 0x32, 0x32, // Body color (dark gray)
    0xFF, 0xFF, 0xFF, // Button color (white)
    0x32, 0x32, 0x32, // Left grip color
    0x32, 0x32, 0x32  // Right grip color
};

// Factory stick calibration at 0x603D (left stick, then right stick)
static const uint8_t factory_stick_cal[] = {
    // Left stick: center, min, max encoded in 3-byte groups
    0x00, 0x08, 0x80,  // Left max above center
    0x00, 0x08, 0x80,  // Left center
    0x00, 0x08, 0x80,  // Left min below center
    // Right stick
    0x00, 0x08, 0x80,
    0x00, 0x08, 0x80,
    0x00, 0x08, 0x80,
    // Deadzone and range ratio
    0x0A, 0x0A, 0x0A, 0x0A, 0x00, 0x00, 0x00, 0x00, 0x00
};

// Factory IMU calibration at 0x6020
static const uint8_t factory_imu_cal[] = {
    // Accel origin (x, y, z) - int16 LE
    0x00, 0x00, 0x00, 0x00, 0x00, 0x40,
    // Accel sensitivity
    0x00, 0x40, 0x00, 0x40, 0x00, 0x40,
    // Gyro origin
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    // Gyro sensitivity
    0x3B, 0x34, 0x3B, 0x34, 0x3B, 0x34
};

// ============================================================================
// Bluetooth HID callbacks and initialization
// ============================================================================

static void packet_handler(uint8_t packet_type, uint16_t channel,
                           uint8_t *packet, uint16_t size) {
    (void)channel;

    if (packet_type == HCI_EVENT_PACKET) {
        switch (hci_event_packet_get_type(packet)) {
            case HCI_EVENT_HID_META: {
                uint8_t subevent = hci_event_hid_meta_get_subevent_code(packet);
                switch (subevent) {
                    case HID_SUBEVENT_CONNECTION_OPENED:
                        connected = true;
                        hid_cid = hid_subevent_connection_opened_get_hid_cid(packet);
                        break;

                    case HID_SUBEVENT_CONNECTION_CLOSED:
                        connected = false;
                        hid_cid = 0;
                        break;

                    case HID_SUBEVENT_CAN_SEND_NOW:
                        // Ready to send - handled in main loop
                        break;
                }
                break;
            }
        }
    }

    // Handle output reports from the Switch (subcommands)
    if (packet_type == HID_REPORT && size > 0) {
        switch_protocol_handle_output(packet, size);
    }
}

void bt_hid_init(void) {
    // Set Bluetooth device name to match Pro Controller
    gap_set_local_name("Pro Controller");
    gap_discoverable_control(1);
    gap_connectable_control(1);

    // Set device class to Gamepad
    // Class of Device: 0x002508 (Peripheral, Gamepad)
    gap_set_class_of_device(0x002508);

    // Initialize HID Device
    hid_device_init(0, sizeof(hid_descriptor), hid_descriptor);

    // Register packet handler
    hid_device_register_packet_handler(&packet_handler);

    // Set HID device info (VID/PID matching Pro Controller)
    // VID: 0x057E (Nintendo), PID: 0x2009 (Pro Controller)
    // These are set via the SDP record

    // Turn on Bluetooth
    hci_power_control(HCI_POWER_ON);
}

void bt_hid_send_input_report(const controller_state_t *state) {
    if (!connected || hid_cid == 0) return;

    uint8_t report[49]; // 0x30 report is 49 bytes (excluding report ID in some stacks)
    memset(report, 0, sizeof(report));

    switch_protocol_build_input_report(report, state);

    // Send via HID interrupt channel
    hid_device_send_interrupt_message(hid_cid, report, sizeof(report));
    hid_device_request_can_send_now_event(hid_cid);
}

bool bt_hid_is_connected(void) {
    return connected;
}
