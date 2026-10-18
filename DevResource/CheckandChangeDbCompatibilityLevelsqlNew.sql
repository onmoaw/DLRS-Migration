


-- Deprecated
-- Check current compatibility level
/*
SELECT name, compatibility_level 
FROM sys.databases 
WHERE name = 'YourDatabaseName';
*/
--*************************************************************


-- check all databased compatibility level in the server by running the following query
-- Check compatibility level for all databases
SELECT 
    name AS 'Database Name',
    compatibility_level,
    CASE compatibility_level
        WHEN 80 THEN 'SQL Server 2000'
        WHEN 90 THEN 'SQL Server 2005'
        WHEN 100 THEN 'SQL Server 2008/R2'
        WHEN 110 THEN 'SQL Server 2012'
        WHEN 120 THEN 'SQL Server 2014'
        WHEN 130 THEN 'SQL Server 2016'
        WHEN 140 THEN 'SQL Server 2017'
        WHEN 150 THEN 'SQL Server 2019'
        WHEN 160 THEN 'SQL Server 2022'
        ELSE 'Unknown'
    END AS 'SQL Server Version'
FROM sys.databases
ORDER BY name;
--**************************************************************************************************

--*************************** Run the following code after running the above query

-- Change compatibility level to use OPENJSON (requires 130 or higher)
------------------------------------------------ TEMPLATE-------------------------------------------
/*

ALTER DATABASE yourdatabasename 
SET COMPATIBILITY_LEVEL = 160;  -- For SQL Server 2022 features

-- Or SET COMPATIBILITY_LEVEL = 130; for just OPENJSON

*/
--********************************************** END *****************************************************



--*********** Add rowid in tblLinfo02 so when updating we can perform update operation with a unique identifier
------------------------------------------------ TEMPLATE-------------------------------------------
/*

-- Add identity row
ALTER TABLE yourdatabasename.dbo.tblLinfo02
ADD RowId INT;


******************************************

WITH OrderedData AS
(
    SELECT *,
           ROW_NUMBER() OVER (
               ORDER BY UnitCod, KhtNum, OwnrGrp, OwnrNum
           ) AS rn
    FROM yourdatabasename.dbo.tblLinfo02
)
UPDATE OrderedData
SET RowId = rn;

*/
--******************************************** END ********************************************************

--************** Change Kreamrk Column type from Text to Nvarchar(max) in tblLinfo01
------------------------------------------------ TEMPLATE-------------------------------------------
/*

alter table yourdatabasename.dbo.tbllinfo01
alter column KRemark nvarchar(max) null;

*/

--******************************************** END ********************************************************


--******************* Change length of some columns in tblLinfo02 ***************************************
------------------------------------------------ TEMPLATE-------------------------------------------
/*

-- alter column length
DECLARE @sql NVARCHAR(MAX) = '';
DECLARE @columns TABLE (colname NVARCHAR(50));

INSERT INTO @columns VALUES ('OwnName'),('addr1'),('addr2'),('addr3'),('addr4'),('addr5'),('addr6'),('addr7'),('addr8'),('addr9'),('addr10');

SELECT @sql = @sql + 'ALTER TABLE yourdatabasename.dbo.tblLinfo02 ALTER COLUMN ' + colname + ' nvarchar(150) NULL;' + CHAR(13)
FROM @columns;

EXEC sp_executesql @sql;

*/
--******************************************** END ********************************************************

--******************************* Add Conversion Status column to three tables **************************************
-----------------------------------------------------------------------------------------------------------------
/*

ALTER TABLE nowakhali.dbo.tblHalsabek
ADD convsts NVARCHAR(20) NOT NULL DEFAULT '';

ALTER TABLE yourdatabasename.dbo.tblLinfo01
ADD convsts NVARCHAR(20) NOT NULL DEFAULT '';

ALTER TABLE yourdatabasename.dbo.tblLinfo02
ADD convsts NVARCHAR(20) NOT NULL DEFAULT '';

*/

