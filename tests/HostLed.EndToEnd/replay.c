// SPDX-License-Identifier: GPL-2.0-or-later
// Runs the real firmware adapter and protocol with hardware-only test doubles.
#include "qmk_stub.h"
#include "host_led_protocol.h"
#include "led_map.h"
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

test_led_config_t g_led_config;
static uint8_t colors[108][3], touched[108], brightness, enabled;
static uint32_t now;
static unsigned record_number;
static void check(int ok, const char *message) {
    if (!ok) { fprintf(stderr, "FAIL record %u: %s\n", record_number, message); exit(1); }
}
uint32_t timer_read32(void) { return now; }
uint8_t rgb_matrix_is_enabled(void) { return enabled; }
uint8_t rgb_matrix_get_val(void) { return brightness; }
void rgb_matrix_set_color(int i, uint8_t r, uint8_t g, uint8_t b) {
    check(i >= 0 && i < 108, "LED index");
    check(!touched[i], "duplicate overlay write");
    touched[i] = 1; colors[i][0] = r; colors[i][1] = g; colors[i][2] = b;
}
void via_custom_value_command_kb(uint8_t *, uint8_t);
void housekeeping_task_user(void);
bool rgb_matrix_indicators_advanced_user(uint8_t, uint8_t);
static void read_bytes(FILE *file, void *data, size_t length) {
    check(fread(data, 1, length, file) == length, "truncated trace");
}
int main(int argc, char **argv) {
    check(argc == 2, "usage: replay trace.bin");
    FILE *file = fopen(argv[1], "rb"); check(file != NULL, "open trace");
    uint8_t map[6][21]; read_bytes(file, map, sizeof(map));
    check(memcmp(map, led_map, sizeof(map)) == 0, "firmware test map differs from board info.json");
    memcpy(g_led_config.matrix_co, map, sizeof(map));
    unsigned packets = 0, frames = 0;
    for (;;) {
        int kind = fgetc(file);
        if (kind == EOF) { check(!ferror(file), "read trace"); break; }
        ++record_number;
        if (kind == 1) {
            uint8_t request[32], expected[32], mask[32];
            read_bytes(file, request, 32); read_bytes(file, expected, 32); read_bytes(file, mask, 32);
            via_custom_value_command_kb(request, 32);
            for (unsigned i = 0; i < 32; ++i) {
                if ((request[i] & mask[i]) != (expected[i] & mask[i])) {
                    fprintf(stderr, "reply byte %u: got %u expected %u\n", i, request[i], expected[i]);
                    check(0, "reply mismatch");
                }
            }
            ++packets;
        } else if (kind == 2) {
            uint8_t expected[108][4];
            read_bytes(file, &brightness, 1); read_bytes(file, &enabled, 1);
            read_bytes(file, expected, sizeof(expected));
            memset(colors, 0x5A, sizeof(colors)); memset(touched, 0, sizeof(touched));
            rgb_matrix_indicators_advanced_user(0, 22);
            for (unsigned i = 0; i < 108; ++i) check(touched[i] == 0, "partial-frame overlay");
            rgb_matrix_indicators_advanced_user(88, 108);
            for (unsigned i = 0; i < 108; ++i) {
                check(touched[i] == expected[i][0], "override/release mismatch");
                for (unsigned c = 0; c < 3; ++c)
                    check(colors[i][c] == (expected[i][0] ? expected[i][c + 1] : 0x5A), "rendered color mismatch");
            }
            ++frames;
        } else if (kind == 3) {
            uint8_t bytes[4]; read_bytes(file, bytes, 4);
            now = (uint32_t)bytes[0] | (uint32_t)bytes[1] << 8 | (uint32_t)bytes[2] << 16 | (uint32_t)bytes[3] << 24;
            housekeeping_task_user();
        } else check(0, "unknown trace record");
    }
    fclose(file);
    check(packets > 0 && frames > 0, "empty trace");
    printf("PASS end-to-end: %u HID requests, %u full LED frame checks (ASan/UBSan)\n", packets, frames);
    return 0;
}
