Fog is easy the fog faster is f. f= 1-e^-(d*p) d is the distance p is the density.  E is eulers constant around 2.7. Final Color = (Scene Color * (1 - f)) + (Fog Color * f). theres some examples like linear(d*p) exponential e^(-d) squared exponental e^(-d^2). 
Reduces density as the y coordinate increases so the fog stays on the ground.
Uses math (like Perlin noise) to make the fog look patchy or swirling.
Calculates light hitting the fog particles to create "God Rays."