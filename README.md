# LightPLU
 Light Intensity converter via PLU(Physical Light Units) for URP

## How to Use
1. Attach LightPLU Component to Light Object
2. Adjust Light Intensity via component
3. Adjust PostProcessing Post Exposure
4. Add Tonemapping and set to ACES

## Samples

![image](https://github.com/8izips/LightPLU/blob/images/sampleCompare.png)
Sample Scene
HDRP(right)
	Lux 120000
	6570 Kelvin
	Exposure 15
	Tonemapping ACES
URP(left)
	Intensity 1.05
	65700 Kelvin
	Post Exposure 0
	Tonemapping ACES
 
Classic Sponza
![image](https://github.com/8izips/LightPLU/blob/images/sponzaCompare.png)
HDRP(upper right)
	Lux 105000
	5500 Kelvin
	Exposure 12
	Tonemapping ACES
URP(upper left)
	Intensity 0.91875
	5500 Kelvin
	Post Exposure 3
	Tonemapping ACES

lower left is default Sponza URP
