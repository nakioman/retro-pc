#include "RetroFloppySerial.h"

RetroFloppySerial::RetroFloppySerial(HardwareSerial &port) : serialPort(port) {}

void RetroFloppySerial::setup() {
  serialPort.begin(SERIAL_BAUD);
}

void RetroFloppySerial::write(const char* format, ...) {
  char tempBuffer[128];
  va_list args;
  
  va_start(args, format); 
  vsnprintf(tempBuffer, sizeof(tempBuffer), format, args);  
  va_end(args);
  
  serialPort.println(tempBuffer);
}

bool RetroFloppySerial::read(String &command) {
  if(serialPort.available()) {
    command = serialPort.readStringUntil(COMMAND_TERMINATOR);
    return command != "";
  }

  return false;
}

void RetroFloppySerial::sendInit(unsigned int version) {
  write("INIT %d\r\n", version);
}
