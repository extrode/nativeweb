-- Spec 024: the five tables the permission layer reads, and the first operator's
-- grant.
--
-- Owned by the consuming application, not by the framework. NativeWeb ships no
-- migration runner and creates no table; libs/nativeweb.permissions defines the
-- IPermissionStore seam and apps/sample implements it over these. The same DDL is
-- kept as a reference at docs/specs/024-model-permissions/024-schema.sql, which
-- nothing applies.
--
-- Every statement is idempotent, because db/migrate.sh re-runs the file.
--
-- The subject is the provider's opaque identifier -- the NameIdentifier claim,
-- whatever issued it. Free text rather than a foreign key to a user table, because
-- R17 is that the permission layer does not know how anyone signed in and this
-- repository holds no identity schema (see the scope boundary in AGENTS.md). The
-- only personal datum here is that string.
--
-- TWO GRANT TABLES, not one with two nullable columns and a CHECK. A single table
-- would let a row name both a subject and a group, or neither, and the constraint
-- keeping it well-formed is exactly the constraint two tables give for free.

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

-- BOOTSTRAP. A fresh database has nobody holding a permission, so nobody can reach
-- the access admin to make the first grant, so the admin is unreachable forever.
-- Every application on this layer has to seed its own first operator; this is the
-- sample's.
--
-- `operator` is the subject apps/sample/AdminSignIn.cs issues for the passphrase
-- sign-in and `demo` is the one Program.cs issues on the Development-only /signin.
-- Both are seeded here because both exist in the sample, and neither name means
-- anything outside it -- a real application seeds the subject its own provider
-- issues, which is a value it has to look up, not a name it can guess.
-- One statement, and the CTE is the point of it. A grant seed keyed on the two names
-- alone would reinstate a grant an operator had revoked -- revoke delete from demo,
-- re-apply, and demo can delete again -- so RETURNING yields only the subjects this
-- run created and the grants are seeded on first sight and never again.
--
-- db/migrate.sh does NOT re-apply this file: is_tracked() (db/migrate.sh:209) skips
-- any version already in schema_migrations. This comment used to claim the opposite,
-- and so did the summary on MigrationRerunTests. The guard is kept anyway, because
-- re-applying a migration by hand is a thing people do to a database they think is
-- behind, and the tier holds the file to that contract.
--
-- KNOWN GAP, from spec 026. ForgetSubjectAsync deletes the nw_subject row, and this
-- guard reads a missing row as first sight. Forget `operator` or `demo`, re-apply
-- this file by hand, and all four grants come back. Closing it needs a marker the
-- forget cannot remove, which is a new migration and a schema change, so it is a
-- note on work/todo.md rather than an edit to an applied file.
WITH seeded AS (
    INSERT INTO nw_subject (subject)
    VALUES ('operator'), ('demo')
    ON CONFLICT (subject) DO NOTHING
    RETURNING subject
)
INSERT INTO nw_subject_grant (subject, permission)
SELECT s.subject, p.permission
FROM seeded s
CROSS JOIN (VALUES
    ('sample:article.view'),
    ('sample:article.add'),
    ('sample:article.change'),
    ('sample:article.delete')
) AS p (permission)
ON CONFLICT (subject, permission) DO NOTHING;
