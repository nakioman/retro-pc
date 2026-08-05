#ifndef RETRO_FLOPPY_SERIAL_H
#define RETRO_FLOPPY_SERIAL_H

#include <Arduino.h>

class RetroFloppySerial {
  private:
    static constexpr unsigned long SERIAL_BAUD = 115200;
    static constexpr char COMMAND_TERMINATOR = '\n';

    HardwareSerial &serialPort;

  public:
    RetroFloppySerial(HardwareSerial &port = Serial);

    void setup();
    void write(const __FlashStringHelper* format, ...);
    bool read(char* buffer, size_t size);
    void sendInit(unsigned int version);
};

#endif
