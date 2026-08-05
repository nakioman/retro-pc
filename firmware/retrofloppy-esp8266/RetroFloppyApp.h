#ifndef RETRO_FLOPPY_APP_H
#define RETRO_FLOPPY_APP_H

#define FLOPPY_LOADED_PIN D6

#include <Arduino.h>
#include <Bounce2.h> 

#include "RetroFloppyNFC.h"
#include "RetroFloppyCommandParser.h"
#include "RetroFloppySerial.h"
#include "RetroFloppyCommandHandler.h"

class RetroFloppyApp {
  private:
    static constexpr const unsigned int PROTOCOL_VERSION = 1;
    static constexpr const unsigned int FLOPPY_DETECT_BTN_INTERVAL = 5;
    Bounce2::Button detectFloppyBtn = Bounce2::Button();

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