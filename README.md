# LightPLU
 Light Intensity converter via PLU(Physical Light Units) for URP

![image](https://github.com/8izips/LightPLU/blob/images/lightPLU_ui.png)

## How to Use
1. Attach LightPLU Component to Light Object
2. Adjust Light Intensity via component
3. Adjust PostProcessing Post Exposure
4. Add Tonemapping and set to ACES

## Samples

Sample Scene
![image](https://github.com/8izips/LightPLU/blob/images/sampleCompare.png)

| URP(left) | HDRP(right) |
| --- | --- |
| Intensity 1.05 | Lux 120000 |
| 6570 Kelvin | 6570 Kelvin |
| Post Exposure 0 | Exposure 15 |
| Tonemapping ACES | Tonemapping ACES |

Classic Sponza
![image](https://github.com/8izips/LightPLU/blob/images/sponzaCompare.png)

| URP(left) | HDRP(right) |
| --- | --- |
| Intensity 0.91875 | Lux 105000 |
| 5500 Kelvin | 5500 Kelvin |
| Post Exposure 3 | Exposure 12 |
| Tonemapping ACES | Tonemapping ACES |

lower left is default Sponza URP
