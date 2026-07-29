constexpr unsigned long SerialBaud = 115200;

String inputLine;

void setup() {
  Serial.begin(SerialBaud);
  Serial.println("READY retrofloppy-esp8266 0.1");
}

void loop() {
  while (Serial.available() > 0) {
    const char character = static_cast<char>(Serial.read());

    if (character == '\r') {
      continue;
    }

    if (character == '\n') {
      handleCommand(inputLine);
      inputLine = "";
      continue;
    }

    inputLine += character;
  }
}

void handleCommand(const String& command) {
  if (command == "PING") {
    Serial.println("PONG");
  } else if (command.length() > 0) {
    Serial.println("ERR unknown-command");
  }
}
