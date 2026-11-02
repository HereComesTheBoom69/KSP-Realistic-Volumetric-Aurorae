// Realistic Volumetric Aurorae (RVA). Copyright (c) 2026 HereComesTheBoom ("Realistic Volumetric Aurorae")
// SPDX-License-Identifier: CC-BY-NC-SA-4.0  (see LICENSE.txt)
using System;

namespace RVA
{

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
            if (dSun <= 0.0 || dPar <= 0.0 || dPar >= dSun) return 0f;

            double aSun = Math.Asin(Math.Min(sun.Radius    / dSun, 1.0));
            double aPar = Math.Asin(Math.Min(parent.Radius / dPar, 1.0));
            double cos  = Vector3d.Dot(toSun, toPar) / (dSun * dPar);
            double sep  = Math.Acos(Math.Max(-1.0, Math.Min(1.0, cos)));
            if (sep >= aSun + aPar) return 0f;
            if (aSun <= 0.0) return 1f;
            double c = (aSun + aPar - sep) / (2.0 * aSun);
            return (float)Math.Max(0.0, Math.Min(1.0, c));
        }
    }
}
