#include "RetroFloppyCommandHandler.h"
#include "RetroFloppyCommands.h"

RetroFloppyCommandHandler::RetroFloppyCommandHandler(RetroFloppySerial &port, RetroFloppyNFC &nfc)
  : serial(port), nfcModule(nfc) {}

void RetroFloppyCommandHandler::execute(const Command &cmd) {
  switch (cmd.type) {
    case CommandType::WRITE:
      {
        if (nfcModule.write(cmd.args)) {
          serial.write("OK");
        } else {
          serial.write("ERROR not written");
        }
        break;
      }
    case CommandType::INSERT:
      {
        serial.write("INSERT %s", cmd.args);
        break;
      }
    case CommandType::TAGID:
      {
        String id = nfcModule.readCardId();
        if (id.length() == 0) {
          serial.write("ERROR no-tag-detected");
        } else {
          serial.write("Tag ID: %s", id.c_str());
        }
        break;
      }
    default:
      {
        serial.write("ERROR %s", cmd.str);
        break;
      }
  }
}