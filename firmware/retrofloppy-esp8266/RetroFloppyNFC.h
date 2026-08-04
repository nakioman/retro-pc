#ifndef RETRO_FLOPPY_NFC_H
#define RETRO_FLOPPY_NFC_H

#include <Arduino.h>
#include <Wire.h>
#include <PN532_I2C.h>
#include <PN532.h>
#include "RetroFloppyCommands.h"

class RetroFloppyNFC {
private:
  PN532_I2C pn532_i2c;
  PN532 nfc;

  static constexpr uint8_t FIRST_PAGE = 4;
  static constexpr uint8_t PAGE_COUNT = 8;
  static constexpr uint8_t BYTES_PER_PAGE = 4;

  bool detectCard(uint8_t *uid, uint8_t &uidLength);
public:
  RetroFloppyNFC();

  void setup();
  bool readCardId(char* out, size_t size);

  bool write(const char* text);
  bool readTag(char* out, size_t size);
};

#endif
