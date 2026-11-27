-- Azure SQL migration 0009: the data location key counts the locations in service.
-- Written by hand. Every statement checks before it acts, so the script can run again after a stop part way.
--
-- The store key of O25 Data Location, Data Platform and External Id, now counts the locations in service: no Retired
-- On, and a status meaning other than archived (Product.Azure.Stores, StoreKeys.cs, StoreKeyScope.InServiceRows), as
-- the engine counts them. Before this build the key counted every location with no Retired On, so a draft location
-- that was withdrawn (archived, with no Retired On) kept its value, and adding the same External Id again failed with
-- an internal error. This script removes the key rows of the locations that are archived. The store writes every later
-- change under the new scope. No engine row changes. The script reads the locations as the deployment identity, which
-- the row-level security filter lets through.

DELETE [k]
FROM [host].[StoreKey] AS [k]
JOIN [ops].[DataLocation] AS [r] ON [r].[Id] = [k].[RowId]
WHERE [k].[Entity] = 'data-location'
    AND [k].[KeyName] = 'Data Platform+External Id'
    AND JSON_VALUE([r].[Columns], N'$.statusMeaning') = N'archived';
GO
