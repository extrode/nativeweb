-- @params subject:string
-- First sight of a subject, recorded once. ON CONFLICT DO NOTHING rather than an
-- upsert: first_seen means first, and a returning visitor must not move it.
INSERT INTO nw_subject (subject)
VALUES (@subject)
ON CONFLICT (subject) DO NOTHING;
