#include "RetroFloppyApp.h"

RetroFloppyApp::RetroFloppyApp(): nfcModule(), serial(), commandParser(), commandHandler(serial, nfcModule) {}

void RetroFloppyApp::setup() {
  serial.setup();
  nfcModule.setup();

  serial.sendInit(PROTOCOL_VERSION);
}

void RetroFloppyApp::update() {
  Command cmd;
  String cmdStr;
  bool isValidCommand = false;

  if (serial.read(cmdStr)) {
    isValidCommand = commandParser.readCommand(cmdStr, cmd);
  }
  else if(nfcModule.readTag(cmdStr)) {
    cmd.type = CommandType::INSERT;
    cmd.args = cmdStr;
    cmd.str = cmdStr;
    cmd.isValid = true;
    isValidCommand = true;
  }

  if(isValidCommand) {
    commandHandler.execute(cmd);
  }
}