#include "RetroFloppyCommandParser.h"

RetroFloppyCommandParser::RetroFloppyCommandParser() {}

bool RetroFloppyCommandParser::parseCommand(const String &cmdStr, String &outVerb, String &outArgs)
{
  int space = cmdStr.indexOf(VERB_DELIMITER);

  if (space == -1)
  {
    outVerb = cmdStr;
    outVerb.trim();
    return true;
  }

  outVerb = cmdStr.substring(0, space);
  outVerb.trim();

  outArgs = cmdStr.substring(space + 1);
  outArgs.trim();

  return true;
}

CommandType RetroFloppyCommandParser::parseType(const String &verb) {
  if(verb == "WRITE") return CommandType::WRITE;
  if(verb == "INSERT") return CommandType::INSERT;
  if(verb == "TAGID") return CommandType::TAGID;

  return CommandType::ERROR;
}

bool RetroFloppyCommandParser::readCommand(const String &cmdStr, Command &out)
{
  String verb;
  String args;

  if (parseCommand(cmdStr, verb, args))
  {
    out.type = parseType(verb);
    out.isValid = out.type != CommandType::ERROR;
    out.args = args;
    out.str = cmdStr;

    return true;
  }

  return false;
}