#include "RetroFloppyNFC.h"

RetroFloppyNFC::RetroFloppyNFC()
  : pn532_i2c(Wire), nfc(pn532_i2c) {}

void RetroFloppyNFC::setup() {
  nfc.begin();
  nfc.SAMConfig();
}

bool RetroFloppyNFC::detectCard(uint8_t* uid, uint8_t& uidLength) {
  return nfc.readPassiveTargetID(PN532_MIFARE_ISO14443A, uid, &uidLength, 1000);
}

String RetroFloppyNFC::readCardId() {
  uint8_t uid[7];
  uint8_t uidLength;

  if (detectCard(uid, uidLength)) {
    String cardID = "";
    for (uint8_t i = 0; i < uidLength; i++) {
      if (uid[i] < 0x10) cardID += "0";
      cardID += String(uid[i], HEX);
    }
    cardID.toUpperCase();
    nfc.inRelease();
    return cardID;
  }
  return "";
}

bool RetroFloppyNFC::write(const String& text) {
  uint8_t uid[7];
  uint8_t uidLength;

  if (!detectCard(uid, uidLength)) {
    return false;
  }

  constexpr size_t maxLength = PAGE_COUNT * 4;

  if (text.length() > maxLength) {
    nfc.inRelease();
    return false;
  }

  for (uint8_t page = FIRST_PAGE;
       page < FIRST_PAGE + PAGE_COUNT;
       ++page) {
    uint8_t data[4] = { 0, 0, 0, 0 };
    size_t offset = (page - FIRST_PAGE) * 4;

    for (uint8_t i = 0; i < 4 && offset + i < text.length(); ++i) {
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

bool RetroFloppyNFC::readTag(String& out) {
  uint8_t uid[7];
  uint8_t uidLength;

  if (!detectCard(uid, uidLength)) {
    return false;
  }

  out = "";

  for (uint8_t page = FIRST_PAGE;
       page < FIRST_PAGE + PAGE_COUNT;
       ++page) {
    uint8_t data[4];

    if (!nfc.mifareultralight_ReadPage(page, data)) {
      nfc.inRelease();
      return false;
    }

    for (uint8_t i = 0; i < 4; ++i) {
      if (data[i] == '\0') {
        out.trim();
        nfc.inRelease();
        return out.length() > 0;
      }

      out += static_cast<char>(data[i]);
    }
  }

  nfc.inRelease();
  out.trim();
  return out.length() > 0;
}
