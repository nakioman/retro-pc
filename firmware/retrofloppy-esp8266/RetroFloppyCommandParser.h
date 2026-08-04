#ifndef RETRO_FLOPPY_COMMAND_PARSER_H
#define RETRO_FLOPPY_COMMAND_PARSER_H

#include <Arduino.h>
#include "RetroFloppyCommands.h"

class RetroFloppyCommandParser {
  private:
    static constexpr const char* VERB_DELIMITER = " ";
  
    bool parseCommand(const String &cmdStr, String& outVerb, String& outArgs);
    CommandType parseType(const String &verb);

  public:
    RetroFloppyCommandParser();

    bool readCommand(const String &cmdStr, Command &out);
};

#endif