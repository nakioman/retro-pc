#ifndef RETRO_FLOPPY_APP_H
#define RETRO_FLOPPY_APP_H

#include <Arduino.h>

#include "RetroFloppyNFC.h"
#include "RetroFloppyCommandParser.h"
#include "RetroFloppySerial.h"
#include "RetroFloppyCommandHandler.h"

class RetroFloppyApp {
  private:
    enum class FloppyState { EMPTY, UNREADABLE, INSERTED };

    static constexpr const unsigned int PROTOCOL_VERSION = 1;
    // With a disk seated the poll is a cheap presence check, so it can run
    // fast to keep eject latency low; the empty-drive poll does a full
    // detect+read and can afford to be slower.
    static constexpr unsigned long POLL_INTERVAL_INSERTED_MS = 100;
    static constexpr unsigned long POLL_INTERVAL_EMPTY_MS = 250;
    // A seated tag can miss a single poll, so only a sustained absence
    // counts as an eject (~300-400 ms at the inserted-poll rate).
    static constexpr uint8_t EJECT_MISS_THRESHOLD = 3;
    // A disk sliding in can couple before it reads cleanly; report an
    // unreadable tag only after it has stayed unreadable for a full second.
    static constexpr uint8_t UNREADABLE_POLL_THRESHOLD = 4;

    RetroFloppyCommandParser commandParser;
    RetroFloppyNFC nfcModule;
    RetroFloppySerial serial;
    RetroFloppyCommandHandler commandHandler;

    FloppyState floppyState = FloppyState::EMPTY;
    unsigned long lastPollAt = 0;
    uint8_t missedPolls = 0;
    uint8_t unreadablePolls = 0;
    char insertedPayload[MAX_COMMAND_LENGTH + 1] = { 0 };

    void pollFloppy();
    void announceInsert(const char* payload);
    void announceEject();
    void refreshInsertedPayload();
    void handleStatus();

  public:
    RetroFloppyApp();

    void setup();
    void update();
};

#endif
