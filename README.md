Similar To Philips AmbiLight.

Works with both adressable and non adressable rgb leds.

This app works by connecting to a microcontroller via WiFi (UDP) or Serial USB port, and streams the color data in raw bytes.
Rest is handled by the microcontroller by your logic. 

The wireless port streams the static color changes to port 7777, and animated / ambilight color data to port 7778.

The app includes Color Calibration for your leds, 3 color calibration presets. For color calibration, eyeball your leds to display white at 4 different brightnesses. Min brightness is your black, if for some reason your leds are completely off at higher rgb values.

By default, wireless connection is set to 192.168.1.100, this is your microcontroller's local IP. Setting a static IP for your microcontroller is recommended.
