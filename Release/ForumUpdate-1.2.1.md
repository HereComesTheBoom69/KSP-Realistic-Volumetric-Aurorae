<!-- reply for the thread. then bump the first posts version and the thread title to v1.2.1
(ForumPost.md). a before/after pair from high orbit at 0.25 makes the point -->

**1.2.1 is up** (SpaceDock, GitHub, CKAN follows).

One fix: the render scale by pixel budget never covered the body you're actually orbiting, only other bodies and the map view. So from a high orbit Kerbin's aurora sat at 0.25 of a screen it filled a tenth of, and the only way to get a sharp band was cranking the render scale slider by hand. Now the scale goes up on its own as the planet shrinks on screen (up to 1.0 at the top) for the same cost as your configured scale filling the view, and nothing changes from the ground or low orbit. The Render scale row in the window tells you what its running at.

Install over 1.2.0, settings file is left alone, shader bundle is the same one. If your still on 1.1.x see the 1.2.0 post above for whats new.
