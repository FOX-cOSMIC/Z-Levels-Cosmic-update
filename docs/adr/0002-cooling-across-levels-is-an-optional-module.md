# Cooling across levels loads only when Rimatomics is active

Z-Levels must still load without Rimatomics, so the cooling module and its defs load only when Rimatomics is active. The module targets both Dubs Rimatomics and Rimatomics++, since Rimatomics++ does not touch the cooling classes. A hard dependency was rejected because it would force every Z-Levels player to install Rimatomics.
