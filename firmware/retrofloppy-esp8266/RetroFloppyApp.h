#ifndef RETRO_FLOPPY_APP_H
#define RETRO_FLOPPY_APP_H

#include <Arduino.h>
#include "RetroFloppyNFC.h"
#include "RetroFloppyCommandParser.h"
#include "RetroFloppySerial.h"
#include "RetroFloppyCommandHandler.h"

class RetroFloppyApp {
  private:
    static constexpr const unsigned int PROTOCOL_VERSION = 1;

    RetroFloppyCommandParser commandParser;
    RetroFloppyNFC nfcModule;
    RetroFloppySerial serial;
    RetroFloppyCommandHandler commandHandler;

  public:
    RetroFloppyApp();

    void setup();
    void update();
};

#endif