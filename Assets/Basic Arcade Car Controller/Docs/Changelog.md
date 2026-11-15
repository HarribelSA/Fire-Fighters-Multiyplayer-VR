# Changelog

## [1.0.0] - Initial Release

### Added
- Basic arcade-style car controller
- Acceleration, braking, and steering system
- Simple tire grip and traction logic
- Fake but controllable gearbox system
- Nitro system
- Counter-steering support
- Center of mass adjustment
- Demo scene
- Example engine sounds

### Notes
- Uses WheelCollider for simplicity
- Designed for learning and prototyping

### Known Issues
* **Throttle Stick:** In some situations, the car may continue accelerating slightly after the throttle is released. This is related to force-based acceleration and can be tuned via drag and damping values.
* **Rollover Risk:** Increasing the "Default Y" value in *Weight Distribution Settings* too much (above 0.3) may cause the car to roll over. Adjusting the `XCOM` value might help, but it is not guaranteed.