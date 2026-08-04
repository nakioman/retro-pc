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

  bool detectCard(uint8_t *uid, uint8_t &uidLength);
public:
  RetroFloppyNFC();

  void setup();
  String readCardId();

  bool write(const String &text);
  bool readTag(String &out);
};

#endif