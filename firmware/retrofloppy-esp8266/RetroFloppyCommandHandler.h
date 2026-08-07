#ifndef RETRO_FLOPPY_COMMAND_HANDLER_H
#define RETRO_FLOPPY_COMMAND_HANDLER_H

#include <Arduino.h>
#include <functional>
#include "RetroFloppySerial.h"
#include "RetroFloppyNFC.h"
#include "RetroFloppyCommands.h"

class RetroFloppyCommandHandler {
  private:
    RetroFloppySerial &serial;
    RetroFloppyNFC &nfcModule;
    std::function<bool()> isFloppyPresent;

  public:
    RetroFloppyCommandHandler(RetroFloppySerial &port, RetroFloppyNFC &nfc, std::function<bool()> floppyPresent);

    void execute(const Command &cmd);
    
};

#endif