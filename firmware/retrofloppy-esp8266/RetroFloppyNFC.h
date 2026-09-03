#ifndef RETRO_FLOPPY_NFC_H
#define RETRO_FLOPPY_NFC_H

#include <Arduino.h>
#include <Wire.h>
#include <PN532_I2C.h>
#include <PN532.h>
#include "RetroFloppyCommands.h"

enum class TagReadResult {
  NO_TAG,       // nothing coupled to the antenna
  READ_FAILED,  // a tag is coupled but its payload could not be read
  OK
};

class RetroFloppyNFC {
private:
  PN532_I2C pn532_i2c;
  PN532 nfc;

  static constexpr uint8_t FIRST_PAGE = 4;
  static constexpr uint8_t PAGE_COUNT = 8;
  static constexpr uint8_t BYTES_PER_PAGE = 4;

  // One activation attempt per poll: the loop provides the retries, so a
  // detect with no tag coupled must return fast instead of blocking serial.
  static constexpr uint8_t PASSIVE_ACTIVATION_RETRIES = 0x01;
  static constexpr uint16_t DETECT_TIMEOUT_MS = 250;

  bool detectCard(uint8_t *uid, uint8_t &uidLength);
public:
  RetroFloppyNFC();

  void setup();
  bool readCardId(char* out, size_t size);

  bool tagPresent();
  bool write(const char* text);
  TagReadResult readTag(char* out, size_t size);
};

#endif
