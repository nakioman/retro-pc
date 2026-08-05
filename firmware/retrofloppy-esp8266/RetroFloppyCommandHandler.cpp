#include "RetroFloppyCommandHandler.h"
#include "RetroFloppyCommands.h"

RetroFloppyCommandHandler::RetroFloppyCommandHandler(RetroFloppySerial &port, RetroFloppyNFC &nfc)
  : serial(port), nfcModule(nfc) {}

void RetroFloppyCommandHandler::execute(const Command &cmd) {
  switch (cmd.type) {
    case CommandType::WRITE:
      {
        if (nfcModule.write(cmd.args())) {
          serial.write(F("OK"));
        } else {
          serial.write(F("ERROR not written"));
        }
        break;
      }
    case CommandType::INSERT:
      {
        serial.write(F("INSERT %s"), cmd.args());
        break;
      }
    case CommandType::TAGID:
      {
        char id[16];
        if (!nfcModule.readCardId(id, sizeof(id))) {
          serial.write(F("ERROR no-tag-detected"));
        } else {
          serial.write(F("Tag ID: %s"), id);
        }
        break;
      }
    case CommandType::EJECT:
      {
        serial.write(F("EJECT"));
        break;
      }
    default:
      {
        serial.write(F("ERROR %s"), cmd.raw);
        break;
      }
  }
}
