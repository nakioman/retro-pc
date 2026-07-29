# retrofloppy-esp8266

Minimal ESP8266 firmware skeleton for the NodeMCU v2 board.

## Setup and upload

Install the tool and ESP8266 Arduino core:

```bash
mise install
mise run firmware-bootstrap
```

Compile only:

```bash
mise run firmware-compile
```

Compile and upload in one command. Replace the serial port with the one used by
the connected board:

```bash
mise run firmware-upload -- /dev/tty.usbserial-XXXX
```

On macOS, the port may look like `/dev/cu.usbserial-XXXX` or
`/dev/cu.SLAB_USBtoUART`.

## Serial monitor test

Open the serial monitor at 115200 baud:

```bash
arduino-cli monitor \
  -p /dev/cu.usbserial-XXXX \
  --config baudrate=115200
```

If `arduino-cli` is not available directly in the shell, run it through mise:

```bash
mise exec -- arduino-cli monitor \
  -p /dev/cu.usbserial-XXXX \
  --config baudrate=115200
```

Press the board's reset button after opening the monitor if the boot message
does not appear. The expected interaction is:

```text
READY retrofloppy-esp8266 0.1
PING
PONG
HELLO
ERR unknown-command
```

Use `Ctrl+C` to exit the monitor. Close the monitor before running
`mise run firmware-upload`, since both commands need exclusive access to the
serial port.

## Serial protocol

Use a serial monitor at 115200 baud with newline line endings. On boot the
firmware prints:

```text
READY retrofloppy-esp8266 0.1
```

Commands:

```text
PING
PONG
```

Any non-empty command other than `PING` returns:

```text
ERR unknown-command
```
