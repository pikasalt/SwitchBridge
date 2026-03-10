cmake_minimum_required(VERSION 3.13)

# Pull in Pico SDK (must be set via PICO_SDK_PATH env var)
include($ENV{PICO_SDK_PATH}/external/pico_sdk_import.cmake)

project(switchbridge_fw C CXX ASM)
set(CMAKE_C_STANDARD 11)
set(CMAKE_CXX_STANDARD 17)

pico_sdk_init()

add_executable(switchbridge_fw
    src/main.c
    src/bt_hid.c
    src/serial_input.c
    src/switch_protocol.c
)

target_include_directories(switchbridge_fw PRIVATE
    ${CMAKE_CURRENT_LIST_DIR}/include
)

target_link_libraries(switchbridge_fw
    pico_stdlib
    pico_cyw43_arch_none        # Bluetooth without WiFi
    pico_btstack_ble
    pico_btstack_classic
    pico_btstack_cyw43
    tinyusb_device              # USB CDC for serial
)

# Enable USB output, disable UART output (we use USB CDC for serial comms)
pico_enable_stdio_usb(switchbridge_fw 0)   # We manage USB ourselves
pico_enable_stdio_uart(switchbridge_fw 0)

pico_add_extra_outputs(switchbridge_fw)
