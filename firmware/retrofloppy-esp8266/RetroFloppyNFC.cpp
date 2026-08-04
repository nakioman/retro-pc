#include "RetroFloppyNFC.h"

namespace {
void trimTrailing(char* text) {
  size_t length = strlen(text);
  while (length > 0) {
    char last = text[length - 1];
    if (last != '\r' && last != '\n' && last != ' ' && last != '\t') break;
    text[--length] = '\0';
  }
}
}  // namespace

RetroFloppyNFC::RetroFloppyNFC()
  : pn532_i2c(Wire), nfc(pn532_i2c) {}

void RetroFloppyNFC::setup() {
  nfc.begin();
  nfc.SAMConfig();
}

bool RetroFloppyNFC::detectCard(uint8_t* uid, uint8_t& uidLength) {
  return nfc.readPassiveTargetID(PN532_MIFARE_ISO14443A, uid, &uidLength, 1000);
}

bool RetroFloppyNFC::readCardId(char* out, size_t size) {
  uint8_t uid[7];
  uint8_t uidLength;

  if (size == 0) return false;
  out[0] = '\0';

  if (!detectCard(uid, uidLength)) {
    return false;
  }

  size_t pos = 0;
  for (uint8_t i = 0; i < uidLength && pos + 2 < size; i++) {
    pos += snprintf(out + pos, size - pos, "%02X", uid[i]);
  }
  out[pos] = '\0';

  nfc.inRelease();
  return pos > 0;
}

bool RetroFloppyNFC::write(const char* text) {
  uint8_t uid[7];
  uint8_t uidLength;

  if (!detectCard(uid, uidLength)) {
    return false;
  }

  size_t textLength = strlen(text);
  constexpr size_t maxLength = PAGE_COUNT * BYTES_PER_PAGE;

  if (textLength > maxLength) {
    nfc.inRelease();
    return false;
  }

  for (uint8_t page = FIRST_PAGE;
       page < FIRST_PAGE + PAGE_COUNT;
       ++page) {
    uint8_t data[BYTES_PER_PAGE] = { 0, 0, 0, 0 };
    size_t offset = (page - FIRST_PAGE) * BYTES_PER_PAGE;

    for (uint8_t i = 0; i < BYTES_PER_PAGE && offset + i < textLength; ++i) {
      data[i] = static_cast<uint8_t>(text[offset + i]);
    }

    if (!nfc.mifareultralight_WritePage(page, data)) {
      nfc.inRelease();
      return false;
    }
  }

  nfc.inRelease();
  return true;
}

bool RetroFloppyNFC::readTag(char* out, size_t size) {
  uint8_t uid[7];
  uint8_t uidLength;

  if (size == 0) return false;

  if (!detectCard(uid, uidLength)) {
    return false;
  }

  size_t pos = 0;
  bool done = false;

  for (uint8_t page = FIRST_PAGE;
       page < FIRST_PAGE + PAGE_COUNT && !done;
       ++page) {
    uint8_t data[BYTES_PER_PAGE];

    if (!nfc.mifareultralight_ReadPage(page, data)) {
      nfc.inRelease();
      return false;
    }

    for (uint8_t i = 0; i < BYTES_PER_PAGE; ++i) {
      if (data[i] == '\0' || pos >= size - 1) {
        done = true;
        break;
      }
      out[pos++] = static_cast<char>(data[i]);
    }
  }

  out[pos] = '\0';
  nfc.inRelease();

  trimTrailing(out);
  return strlen(out) > 0;
}
