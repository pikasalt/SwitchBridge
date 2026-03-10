/**
 * BTStack configuration for SwitchBridge firmware.
 *
 * Enables Classic Bluetooth with HID Device profile for
 * Pro Controller emulation.
 */

#ifndef BTSTACK_CONFIG_H
#define BTSTACK_CONFIG_H

// BTStack features
#define ENABLE_LOG_INFO
#define ENABLE_LOG_ERROR
#define ENABLE_CLASSIC
#define ENABLE_HID_DEVICE
#define ENABLE_L2CAP
#define ENABLE_SDP
#define ENABLE_BTSTACK_STDIN

// Memory config
#define HCI_ACL_PAYLOAD_SIZE        1021
#define MAX_NR_BTSTACK_LINK_KEYS    4
#define MAX_NR_HCI_CONNECTIONS      2
#define MAX_NR_L2CAP_SERVICES       3
#define MAX_NR_L2CAP_CHANNELS       3
#define MAX_NR_SDP_RECORDS          2

// HID specific
#define HID_DEVICE_MAX_DESCRIPTOR_SIZE  150

// CYW43 Bluetooth
#define CYW43_ENABLE_BLUETOOTH      1

#endif // BTSTACK_CONFIG_H
