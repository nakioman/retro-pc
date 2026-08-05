#ifndef RETRO_FLOPPY_COMMAND_PARSER_H
#define RETRO_FLOPPY_COMMAND_PARSER_H

#include <Arduino.h>
#include "RetroFloppyCommands.h"

class RetroFloppyCommandParser {
  private:
    static constexpr char VERB_DELIMITER = ' ';

    CommandType parseType(const char* verb, size_t length);

  public:
    RetroFloppyCommandParser();

    void readCommand(const char* cmdStr, Command &out);
    void makeInsert(const char* text, Command &out);
    void makeError(const char* text, Command &out);
    void makeEject(Command &out);
};

#endif
