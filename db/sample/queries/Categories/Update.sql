-- @params name:string, key:string
UPDATE categories
SET name = @name
WHERE key = @key;
