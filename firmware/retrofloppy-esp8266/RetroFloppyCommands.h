#ifndef RETRO_FLOPPY_COMMANDS_H
#define RETRO_FLOPPY_COMMANDS_H

#include <Arduino.h>

// Upper bound for a full command line (verb + args). NFC payloads are at most
// PAGE_COUNT * BYTES_PER_PAGE (32) bytes, so this comfortably covers both the
// serial and NFC paths without any heap allocation.
static constexpr size_t MAX_COMMAND_LENGTH = 48;

enum class CommandType {
  INSERT,
  WRITE,
  ERROR,
  TAGID,
  EJECT
};

struct Command {
  CommandType type = CommandType::ERROR;
  char raw[MAX_COMMAND_LENGTH + 1] = { 0 };
  uint8_t argsOffset = 0;
  bool isValid = false;

  // View into `raw` pointing at the argument portion (after the verb).
  const char* args() const { return raw + argsOffset; }
};

#endif
