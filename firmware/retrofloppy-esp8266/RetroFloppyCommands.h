#ifndef RETRO_FLOPPY_COMMANDS_H
#define RETRO_FLOPPY_COMMANDS_H

#include <Arduino.h>

enum class CommandType {
  INSERT,
  WRITE,
  EJECT,
  ERROR,
  TAGID
};

enum class FloppyAccessMode {
  READ_ONLY,
  READ_WRITE,
  UNKNOWN
};

struct Command {
  CommandType type = CommandType::ERROR;
  String args;   
  bool isValid = false;
  String str;
};

#endif