#include "RetroFloppySerial.h"

RetroFloppySerial::RetroFloppySerial(HardwareSerial &port) : serialPort(port) {}

void RetroFloppySerial::setup() {
  serialPort.begin(SERIAL_BAUD);
}

void RetroFloppySerial::write(const __FlashStringHelper* format, ...) {
  char tempBuffer[128];
  va_list args;

  va_start(args, format);
  vsnprintf_P(tempBuffer, sizeof(tempBuffer), reinterpret_cast<PGM_P>(format), args);
  va_end(args);

  serialPort.println(tempBuffer);
}

bool RetroFloppySerial::read(char* buffer, size_t size) {
  if (!serialPort.available()) {
    return false;
  }

  size_t length = serialPort.readBytesUntil(COMMAND_TERMINATOR, buffer, size - 1);
  buffer[length] = '\0';

  while (length > 0) {
    char last = buffer[length - 1];
    if (last != '\r' && last != '\n' && last != ' ' && last != '\t') break;
    buffer[--length] = '\0';
  }

  return length > 0;
}

void RetroFloppySerial::sendInit(unsigned int version) {
  write(F("INIT %d\r\n"), version);
}
