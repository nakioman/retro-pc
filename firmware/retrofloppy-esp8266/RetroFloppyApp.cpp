#include <cstdio>
#include "RetroFloppyApp.h"

RetroFloppyApp::RetroFloppyApp()
  : commandParser(), nfcModule(), serial(), commandHandler(serial, nfcModule) {}

void RetroFloppyApp::setup() {
  serial.setup();
  nfcModule.setup();

  detectFloppyBtn.attach(FLOPPY_LOADED_PIN, INPUT_PULLUP);
  detectFloppyBtn.interval(FLOPPY_DETECT_BTN_INTERVAL);

  serial.sendInit(PROTOCOL_VERSION);
}

void RetroFloppyApp::update() {
  Command cmd;
  char cmdStr[MAX_COMMAND_LENGTH + 1];

  detectFloppyBtn.update();

  if (detectFloppyBtn.changed()) {
    bool inserted = (detectFloppyBtn.read() == HIGH);
    if (inserted) {
      if (nfcModule.readTag(cmdStr, sizeof(cmdStr))) {
        commandParser.makeInsert(cmdStr, cmd);
      } else {
        snprintf(cmdStr, sizeof(cmdStr), "TAG not read");
        commandParser.makeError(cmdStr, cmd);
      }
    } else {
      commandParser.makeEject(cmd);
    }
    commandHandler.execute(cmd);
  } else if (serial.read(cmdStr, sizeof(cmdStr))) {
    commandParser.readCommand(cmdStr, cmd);
    commandHandler.execute(cmd);
  }
}
