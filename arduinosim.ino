
struct DeviceState {
  // Soldering Iron
  int solderingTemp = 25;
  int solderingPower = 0;
  bool solderingState = false;

  int smdTemp = 25;
  int smdPower = 0;
  bool smdState = false;
  int smdAirFlow = 0;

  int lcdTemp = 25;
  int lcdPower = 0;
  bool lcdState = false;
  bool vacuumState = false;
};

DeviceState currentState;

void setup() {
  Serial.begin(9600);
  randomSeed(analogRead(0));
}

void sendSerialData() {
  //serial data string format:
  // SolderingIronTemp,SolderingIronPower,SMDReworkTemp,SMDReworkPower,SMDReworkState,SMDReworkAirFlow,LCDRepairTemp,LCDRepairPower,VacuumPumpState
  String serialData = 
    String(currentState.solderingTemp) + "," + 
    String(currentState.solderingPower) + "," + 
    String(currentState.smdTemp) + "," + 
    String(currentState.smdPower) + "," + 
    String(currentState.smdState ? "1" : "0") + "," + 
    String(currentState.smdAirFlow) + "," + 
    String(currentState.lcdTemp) + "," + 
    String(currentState.lcdPower) + "," + 
    String(currentState.vacuumState ? "1" : "0");

  Serial.println(serialData);
}

void processCommand(String command) {

  int firstComma = command.indexOf(',');
  int secondComma = command.indexOf(',', firstComma + 1);
  
  if (firstComma == -1 || secondComma == -1) {
    return;
  }
  
  String device = command.substring(0, firstComma);
  String action = command.substring(firstComma + 1, secondComma);
  String value = command.substring(secondComma + 1);

  if (device == "SI") {  // Soldering Iron
    if (action == "PWR") {
      currentState.solderingState = (value == "1");
      currentState.solderingPower = currentState.solderingState ? 50 : 0;
    }
    else if (action == "SET") {
      currentState.solderingPower = value.toInt();
      currentState.solderingState = currentState.solderingPower > 0;
    }
  }
  else if (device == "SMD") {  // SMD Rework Station
    if (action == "PWR") {
      currentState.smdState = (value == "1");
      currentState.smdPower = currentState.smdState ? 50 : 0;
    }
    else if (action == "SET") {
      currentState.smdPower = value.toInt();
      currentState.smdState = currentState.smdPower > 0;
    }
    else if (action == "AIR") {
      currentState.smdAirFlow = value.toInt();
    }
  }
  else if (device == "LCD") {  // LCD Repair
    if (action == "PWR") {
      currentState.lcdState = (value == "1");
      currentState.lcdPower = currentState.lcdState ? 50 : 0;
    }
    else if (action == "SET") {
      currentState.lcdPower = value.toInt();
      currentState.lcdState = currentState.lcdPower > 0;
    }
    else if (action == "VAC") {
      currentState.vacuumState = (value == "1");
    }
  }

  simulateTemperatureChanges();
}

void simulateTemperatureChanges() {
  // Simulate heating and cooling
  if (currentState.solderingState) {
    currentState.solderingTemp += (currentState.solderingPower / 10);
  } else {
    currentState.solderingTemp = max(25, currentState.solderingTemp - 2);
  }

  if (currentState.smdState) {
    currentState.smdTemp += (currentState.smdPower / 10);
  } else {
    currentState.smdTemp = max(25, currentState.smdTemp - 2);
  }

  if (currentState.lcdState) {
    currentState.lcdTemp += (currentState.lcdPower / 10);
  } else {
    currentState.lcdTemp = max(25, currentState.lcdTemp - 2);
  }

  // Ensure temperatures don't exceed realistic maximums
  currentState.solderingTemp = min(450, currentState.solderingTemp);
  currentState.smdTemp = min(350, currentState.smdTemp);
  currentState.lcdTemp = min(300, currentState.lcdTemp);
}

void loop() {
  simulateTemperatureChanges();
  
  sendSerialData();
  
  delay(100);
}

void serialEvent() {
  while (Serial.available()) {
    String command = Serial.readStringUntil('\n');
    command.trim();
    
    if (command.length() > 0) {
      processCommand(command);
    }
  }
}
