#!/usr/bin/env bash
set -euo pipefail
firmware=$1
output=$2
script_dir=$(cd -- "$(dirname -- "$0")" && pwd)
custom="$firmware/keyboards/keychron/q6_pro/ansi_encoder/keymaps/custom"
gcc -std=c11 -Wall -Wextra -Werror -pedantic -g -fsanitize=address,undefined \
    -I"$custom" -I"$custom/tests" '-DQMK_KEYBOARD_H="qmk_stub.h"' \
    "$custom/host_led_protocol.c" "$custom/host_led_qmk.c" "$script_dir/replay.c" \
    -o "$output/replay"
"$output/replay" "$output/trace.bin"
