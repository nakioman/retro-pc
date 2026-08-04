#include "RetroFloppyApp.h"

RetroFloppyApp::RetroFloppyApp()
  : commandParser(), nfcModule(), serial(), commandHandler(serial, nfcModule) {}

void RetroFloppyApp::setup() {
  serial.setup();
  nfcModule.setup();

  serial.sendInit(PROTOCOL_VERSION);
}

void RetroFloppyApp::update() {
  Command cmd;
  char cmdStr[MAX_COMMAND_LENGTH + 1];

  if (serial.read(cmdStr, sizeof(cmdStr))) {
    commandParser.readCommand(cmdStr, cmd);
    commandHandler.execute(cmd);
  }
  else if (nfcModule.readTag(cmdStr, sizeof(cmdStr))) {
    commandParser.makeInsert(cmdStr, cmd);
    commandHandler.execute(cmd);
  }
}
