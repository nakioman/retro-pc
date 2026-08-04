#ifndef RETRO_FLOPPY_COMMAND_HANDLER_H
#define RETRO_FLOPPY_COMMAND_HANDLER_H

#include <Arduino.h>
#include "RetroFloppySerial.h"
#include "RetroFloppyNFC.h"
#include "RetroFloppyCommands.h"

class RetroFloppyCommandHandler {
  private:
    RetroFloppySerial &serial;
    RetroFloppyNFC &nfcModule;

  public:
    RetroFloppyCommandHandler(RetroFloppySerial &port, RetroFloppyNFC &nfc);

    void execute(const Command &cmd);
    
};

#endif