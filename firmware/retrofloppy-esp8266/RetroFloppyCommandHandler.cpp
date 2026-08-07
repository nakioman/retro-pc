#include "RetroFloppyCommandHandler.h"
#include "RetroFloppyCommands.h"

RetroFloppyCommandHandler::RetroFloppyCommandHandler(RetroFloppySerial &port, RetroFloppyNFC &nfc, std::function<bool()> floppyPresent)
  : serial(port), nfcModule(nfc), isFloppyPresent(floppyPresent) {}

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
    case CommandType::PING:
      {
        serial.write(F("PONG"));
        break;
      }
    case CommandType::STATUS:
      {
        if (!isFloppyPresent || !isFloppyPresent()) {
          serial.write(F("EJECT"));
          break;
        }
        char tag[MAX_COMMAND_LENGTH + 1];
        if (nfcModule.readTag(tag, sizeof(tag))) {
          serial.write(F("INSERT %s"), tag);
        } else {
          serial.write(F("ERROR no-tag-detected"));
        }
        break;
      }
    default:
      {
        serial.write(F("ERROR %s"), cmd.raw);
        break;
      }
  }
}
