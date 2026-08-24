Ambient Traffic Jam - Demo assets
=================================

Cars       : original low-poly prefabs built from Unity primitives (body, cabin,
             windshield, 4 wheels). (c) 2026 fuR Gaming. MIT / free to use and
             redistribute as part of this asset.
Materials  : original URP/Lit materials (Body, Glass, Tyre, TailLight, Asphalt).
             (c) 2026 fuR Gaming. MIT.
Road/scene : original. (c) 2026 fuR Gaming. MIT.
Audio      : Car_Move_1/2/3.wav in Demo/Audio/ are original, procedurally
             synthesized steady engine-idle loops (2s, seamless). (c) 2026
             fuR Gaming. Free to use and redistribute as part of this asset.
             They are intentionally simple placeholders sized for background
             ambience - swap in your own recordings for a richer result.

Using your own move SFX
-----------------------
Assign short, steady, loopable engine beds to the AmbientTraffic component's
"Pass By Clips" array. Each car picks one at random and loops it, fading the
volume in when the car creeps and out when it stops. Keep them low and steady
(no revving/whoosh) so the jam reads as background ambience. Tune loudness with
"Pass By Volume" and audible range with "Pass By Min/Max Distance".
