-- 0001: Extensions. The one migration that must run as a superuser --
-- __product___app cannot CREATE EXTENSION. citext backs case-insensitive
-- usernames, emails and other natural keys.

CREATE EXTENSION IF NOT EXISTS citext;
