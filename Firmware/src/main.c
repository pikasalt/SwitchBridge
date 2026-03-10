/**
 * SwitchBridge Firmware - Main Entry Point
 *
 * Initializes Bluetooth HID (Pro Controller emulation), USB CDC serial input,
 * and runs the main loop that reads controller state from serial and sends
 * it to the Switch via Bluetooth.
 */

#include <stdio.h>
#include "pico/stdlib.h"
#include "pico/cyw43_arch.h"
#include "switchbridge.h"

// LED blink patterns for status indication
#define LED_BLINK_SEARCHING_MS  500
#define LED_BLINK_CONNECTED_MS  2000
#define REPORT_INTERVAL_US      16667  // ~60Hz (16.67ms)

static controller_state_t current_state = {0};

int main(void) {
    // Initialize Pico standard library
    stdio_init_all();

    // Initialize CYW43 (wireless chip - needed for Bluetooth)
    if (cyw43_arch_init()) {
        // Fatal: can't init wireless chip
        while (1) {
            tight_loop_contents();
        }
    }

    // Initialize subsystems
    serial_input_init();
    bt_hid_init();

    // Set default stick positions to center
    current_state.lx = 2048;
    current_state.ly = 2048;
    current_state.rx = 2048;
    current_state.ry = 2048;

    // Main loop
    absolute_time_t next_report = get_absolute_time();
    bool led_state = false;
    absolute_time_t next_led = get_absolute_time();

    while (1) {
        // Read any available serial input
        if (serial_input_read(&current_state)) {
            // Got fresh data - will be sent on next report interval
        }

        // Send input reports at ~60Hz when connected
        if (absolute_time_diff_us(next_report, get_absolute_time()) >= 0) {
            if (bt_hid_is_connected()) {
                bt_hid_send_input_report(&current_state);
            }
            next_report = delayed_by_us(next_report, REPORT_INTERVAL_US);
        }

        // LED status indication
        if (absolute_time_diff_us(next_led, get_absolute_time()) >= 0) {
            led_state = !led_state;
            cyw43_arch_gpio_put(CYW43_WL_GPIO_LED_PIN, led_state);

            uint32_t blink_ms = bt_hid_is_connected()
                ? LED_BLINK_CONNECTED_MS
                : LED_BLINK_SEARCHING_MS;
            next_led = delayed_by_ms(next_led, blink_ms);
        }

        // Give BTStack time to process
        // (In a real BTStack integration, this would be handled by the
        //  BTStack run loop. Simplified here for clarity.)
        tight_loop_contents();
    }

    return 0;
}
