#include "RetroFloppyApp.h"

RetroFloppyApp::RetroFloppyApp()
  : commandParser(), nfcModule(), serial(), commandHandler(serial, nfcModule) {}

void RetroFloppyApp::setup() {
  serial.setup();
  nfcModule.setup();

  serial.sendInit(PROTOCOL_VERSION);
}

void RetroFloppyApp::update() {
  char cmdStr[MAX_COMMAND_LENGTH + 1];

  pollFloppy();

  if (serial.read(cmdStr, sizeof(cmdStr))) {
    Command cmd;
    commandParser.readCommand(cmdStr, cmd);
    if (cmd.type == CommandType::STATUS) {
      handleStatus();
    } else {
      commandHandler.execute(cmd);
      if (cmd.type == CommandType::WRITE) {
        refreshInsertedPayload();
      }
    }
  }
}

// The tag glued inside the floppy shell doubles as the disk-present sensor:
// tag in the PN532 field means inserted, tag gone means ejected.
void RetroFloppyApp::pollFloppy() {
  unsigned long interval = (floppyState == FloppyState::INSERTED)
                             ? POLL_INTERVAL_INSERTED_MS
                             : POLL_INTERVAL_EMPTY_MS;
  unsigned long now = millis();
  if (now - lastPollAt < interval) return;
  lastPollAt = now;

  if (floppyState == FloppyState::INSERTED) {
    if (nfcModule.tagPresent()) {
      missedPolls = 0;
    } else if (++missedPolls >= EJECT_MISS_THRESHOLD) {
      announceEject();
    }
    return;
  }

  char payload[MAX_COMMAND_LENGTH + 1];
  switch (nfcModule.readTag(payload, sizeof(payload))) {
    case TagReadResult::OK:
      announceInsert(payload);
      break;
    case TagReadResult::READ_FAILED:
      missedPolls = 0;
      if (floppyState == FloppyState::EMPTY &&
          ++unreadablePolls >= UNREADABLE_POLL_THRESHOLD) {
        floppyState = FloppyState::UNREADABLE;
        Command cmd;
        commandParser.makeError("TAG not read", cmd);
        commandHandler.execute(cmd);
      }
      break;
    case TagReadResult::NO_TAG:
      unreadablePolls = 0;
      if (floppyState == FloppyState::UNREADABLE &&
          ++missedPolls >= EJECT_MISS_THRESHOLD) {
        announceEject();
      }
      break;
  }
}

void RetroFloppyApp::announceInsert(const char* payload) {
  strlcpy(insertedPayload, payload, sizeof(insertedPayload));
  floppyState = FloppyState::INSERTED;
  missedPolls = 0;
  unreadablePolls = 0;

  Command cmd;
  commandParser.makeInsert(insertedPayload, cmd);
  commandHandler.execute(cmd);
}

void RetroFloppyApp::announceEject() {
  floppyState = FloppyState::EMPTY;
  missedPolls = 0;
  unreadablePolls = 0;
  insertedPayload[0] = '\0';

  Command cmd;
  commandParser.makeEject(cmd);
  commandHandler.execute(cmd);
}

// WRITE rewrites the seated tag, so the payload cached for STATUS has to
// follow it.
void RetroFloppyApp::refreshInsertedPayload() {
  if (floppyState != FloppyState::INSERTED) return;

  char payload[MAX_COMMAND_LENGTH + 1];
  if (nfcModule.readTag(payload, sizeof(payload)) == TagReadResult::OK) {
    strlcpy(insertedPayload, payload, sizeof(insertedPayload));
  }
}

void RetroFloppyApp::handleStatus() {
  switch (floppyState) {
    case FloppyState::INSERTED:
      serial.write(F("INSERT %s"), insertedPayload);
      break;
    case FloppyState::UNREADABLE:
      serial.write(F("ERROR no-tag-detected"));
      break;
    case FloppyState::EMPTY:
      serial.write(F("EJECT"));
      break;
  }
}
