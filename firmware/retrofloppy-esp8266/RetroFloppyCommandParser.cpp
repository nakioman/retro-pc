#include "RetroFloppyCommandParser.h"

namespace {
struct VerbEntry {
  const char* verb;
  CommandType type;
};

const VerbEntry VERB_TABLE[] = {
  { "WRITE", CommandType::WRITE },
  { "INSERT", CommandType::INSERT },
  { "TAGID", CommandType::TAGID },
};

void copyRaw(char* dest, const char* src) {
  strncpy(dest, src, MAX_COMMAND_LENGTH);
  dest[MAX_COMMAND_LENGTH] = '\0';
}
}  // namespace

RetroFloppyCommandParser::RetroFloppyCommandParser() {}

CommandType RetroFloppyCommandParser::parseType(const char* verb, size_t length) {
  for (const VerbEntry &entry : VERB_TABLE) {
    if (strlen(entry.verb) == length && strncmp(verb, entry.verb, length) == 0) {
      return entry.type;
    }
  }

  return CommandType::ERROR;
}

void RetroFloppyCommandParser::readCommand(const char* cmdStr, Command &out) {
  copyRaw(out.raw, cmdStr);

  const char* space = strchr(out.raw, VERB_DELIMITER);
  size_t verbLength = space ? static_cast<size_t>(space - out.raw) : strlen(out.raw);

  size_t offset = verbLength;
  while (out.raw[offset] == VERB_DELIMITER) offset++;
  out.argsOffset = static_cast<uint8_t>(offset);

  out.type = parseType(out.raw, verbLength);
  out.isValid = out.type != CommandType::ERROR;
}

void RetroFloppyCommandParser::makeInsert(const char* text, Command &out) {
  copyRaw(out.raw, text);
  out.type = CommandType::INSERT;
  out.argsOffset = 0;
  out.isValid = true;
}
