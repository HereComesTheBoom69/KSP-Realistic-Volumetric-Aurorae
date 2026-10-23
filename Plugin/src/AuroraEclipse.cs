// Realistic Volumetric Aurorae (RVA) — Copyright (c) 2026 HereComesTheBoom ("Realistic Volumetric Aurorae")
// SPDX-License-Identifier: CC-BY-NC-SA-4.0  (see LICENSE.txt)
using System;

namespace RVA
{
    /// <summary>
    /// How much of the sun a body's parent hides from it (1.1.0). Laythe spends about an
    /// hour of every orbit in Jool's shadow, and while it does an aurora shows over the
    /// whole moon, not just its night side — so the daylight fade is lifted by this much.
    /// 0 = sun fully visible, 1 = total eclipse; the ramp in between is the penumbra
    /// (first contact → totality, linear in angle, ~100 km wide at Laythe).
    /// Only the parent body is tested: it is the one shadow in the stock system that
    /// reaches, and lasts (a moon's shadow on its planet is a dot, and a sibling's never
    /// lines up for long). Positions are KSP's world positions, the same frame for all
    /// three bodies in every scene the plugin runs in.
    /// </summary>
    public static class AuroraEclipse
    {
        public static float Coverage(CelestialBody body, CelestialBody sun)
        {
            if (body == null || sun == null || ReferenceEquals(body, sun)) return 0f;
            CelestialBody parent = body.referenceBody;
            if (parent == null || ReferenceEquals(parent, sun) || ReferenceEquals(parent, body)) return 0f;

            Vector3d toSun = sun.position - body.position;
            Vector3d toPar = parent.position - body.position;
            double dSun = toSun.magnitude, dPar = toPar.magnitude;
            if (dSun <= 0.0 || dPar <= 0.0 || dPar >= dSun) return 0f;     // parent not between us and the sun

            // apparent radii of the two discs and the angle between their centres
            double aSun = Math.Asin(Math.Min(sun.Radius    / dSun, 1.0));
            double aPar = Math.Asin(Math.Min(parent.Radius / dPar, 1.0));
            double cos  = Vector3d.Dot(toSun, toPar) / (dSun * dPar);
            double sep  = Math.Acos(Math.Max(-1.0, Math.Min(1.0, cos)));
            if (sep >= aSun + aPar) return 0f;                                // discs apart
            if (aSun <= 0.0) return 1f;
            double c = (aSun + aPar - sep) / (2.0 * aSun);                    // 0 at first contact, 1 at totality (never 1 if the parent looks smaller than the sun)
            return (float)Math.Max(0.0, Math.Min(1.0, c));
        }
    }
}
