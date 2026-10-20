-- Spec 024: the permission layer's tables, as a reference.
--
-- NOTHING APPLIES THIS FILE. The repository has no migration runner for docs/, and
-- the framework creates no table: libs/nativeweb.permissions defines the
-- IPermissionStore seam and the application owns the storage behind it. This is
-- here so an application adopting the layer has the schema in one place without
-- reading the sample's migration history.
--
-- The applied copy is db/sample/migrations/0006_permissions.sql. The table DDL
-- below is identical to it, character for character. What that file has and this
-- one does not is the bootstrap seed, which is the one part no framework can
-- write: see the template at the end.

CREATE TABLE IF NOT EXISTS nw_subject (
    subject     text        PRIMARY KEY,
    first_seen  timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS nw_group (
    id    bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    name  text   NOT NULL UNIQUE
);

CREATE TABLE IF NOT EXISTS nw_group_member (
    subject   text   NOT NULL REFERENCES nw_subject (subject) ON DELETE CASCADE,
    group_id  bigint NOT NULL REFERENCES nw_group (id) ON DELETE CASCADE,
    PRIMARY KEY (subject, group_id)
);

-- A grant names a permission as text, not as a foreign key to a catalog table.
-- The catalog is a compile-time artefact: it is whatever the generator emitted for
-- this build, and a grant made before a declaration was removed has to survive so
-- the access admin can list it and an operator can revoke it. The intersection at
-- request time is what makes such a row inert (R14).
CREATE TABLE IF NOT EXISTS nw_subject_grant (
    subject     text NOT NULL REFERENCES nw_subject (subject) ON DELETE CASCADE,
    permission  text NOT NULL,
    PRIMARY KEY (subject, permission)
);

CREATE TABLE IF NOT EXISTS nw_group_grant (
    group_id    bigint NOT NULL REFERENCES nw_group (id) ON DELETE CASCADE,
    permission  text   NOT NULL,
    PRIMARY KEY (group_id, permission)
);

-- The membership lookup runs subject-first on every request, which the composite
-- primary key already serves. This index is the other direction: the access admin
-- lists one group's members, and without it that read is a sequential scan.
CREATE INDEX IF NOT EXISTS nw_group_member_group_id_idx
    ON nw_group_member (group_id);

-- BOOTSTRAP, and it is not optional. A fresh database has nobody holding a
-- permission, so nobody can reach the access admin to make the first grant, so the
-- admin is unreachable forever. Every application seeds its own first operator with
-- the subject its own provider issues -- an opaque value it has to look up, not a
-- name it can guess -- and the permission names its own build declared.
--
--   INSERT INTO nw_subject (subject) VALUES ('<subject>')
--   ON CONFLICT (subject) DO NOTHING;
--
--   INSERT INTO nw_subject_grant (subject, permission)
--   VALUES ('<subject>', '<scope>:<model>.<action>')
--   ON CONFLICT (subject, permission) DO NOTHING;
