-- ==============================================================================
-- Stored Procedure: [dbo_lis].[SP_LIS_GET_MIGRATED_RECORDS]
-- Database: DLRSLSINF_4103PRSDB
-- Purpose: Retrieves migrated land records for a given Mouza (UNITCOD),
--          Survey Type (RSNUM), with optional Khatian and Owner filters.
--          Returns 3 synchronized result sets:
--            Result Set 0: Khatians (LINFO00 + LANDTYPE + AREAINF + Plot Aggregates)
--            Result Set 1: Owners (LINFO02)
--            Result Set 2: Plots (LINFO01 + 3-part Land Classification from LANDUSE)
-- ==============================================================================

USE [DLRSLSINF_4103PRSDB]
GO

SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

CREATE OR ALTER PROCEDURE [dbo_lis].[SP_LIS_GET_MIGRATED_RECORDS]
    @ComCod         NVARCHAR(10)  = '4103',
    @RsNum          NVARCHAR(20)  = '201901',
    @UnitCod        NVARCHAR(20),             -- Full 9-digit Mouza code (e.g. '010502003')
    @CurrentKhatian NVARCHAR(50)  = NULL,    -- Optional filter for Khatian number
    @OwnerName      NVARCHAR(200) = NULL     -- Optional filter for Owner name
AS
BEGIN
    SET NOCOUNT ON;

    SET @ComCod = ISNULL(NULLIF(LTRIM(RTRIM(@ComCod)), ''), '4103');
    SET @RsNum = ISNULL(NULLIF(LTRIM(RTRIM(@RsNum)), ''), '201901');
    SET @UnitCod = LTRIM(RTRIM(ISNULL(@UnitCod, '')));
    SET @CurrentKhatian = NULLIF(LTRIM(RTRIM(@CurrentKhatian)), '');
    SET @OwnerName = NULLIF(LTRIM(RTRIM(@OwnerName)), '');

    -- 1. Base Khatians from LINFO00 joined with LANDTYPE and AREAINF
    SELECT 
        lf00.COMCOD,
        lf00.RSNUM,
        lf00.UNITCOD,
        ISNULL(a.UNITNAM, lf00.UNITCOD) AS MouzaName,
        CASE WHEN lf00.RSNUM = '201901' THEN N'বিডিএস (BDS)' ELSE lf00.RSNUM END AS SurveyName,
        lf00.KHTNUM AS CurrentKhatian,
        ISNULL(lf00.PKHTNUM, '') AS FormerKhatian,
        ISNULL(lf00.EKHTNUM, '') AS EntryKhatian,
        ISNULL(lf00.SHETNUM, '') AS SheetNum,
        lf00.LTCODE AS LtCode,
        ISNULL(lt.LTNAME, '') AS LandTypeName,
        ISNULL(lt.PTITLE, '') AS PorchaTitle,
        ISNULL(lf00.KREMARK, '') AS KhatianRemarks,
        ISNULL(lf00.KHTNOTE, '') AS KhatianNote,
        ISNULL(lf00.KHTSTATUS, '') AS KhatianStatus,
        lf00.USRID,
        lf00.SEALID,
        ISNULL(lf00.SEALDESC, '') AS SealDesc,
        lf00.TERMID,
        FORMAT(lf00.ROWTIME, 'dd-MMM-yyyy hh:mm tt') AS EntryTime
    INTO #tblKhatian
    FROM dbo_lis.LINFO00 lf00
    LEFT JOIN dbo.AREAINF a ON lf00.COMCOD = a.COMCOD AND lf00.UNITCOD = a.UNITCOD
    LEFT JOIN dbo.LANDTYPE lt ON lf00.LTCODE = lt.LTCODE
    WHERE lf00.COMCOD = @ComCod
      AND (@RsNum = '%' OR lf00.RSNUM = @RsNum)
      AND (lf00.UNITCOD = @UnitCod OR lf00.UNITCOD LIKE @UnitCod + '%')
      AND (@CurrentKhatian IS NULL OR lf00.KHTNUM LIKE '%' + @CurrentKhatian + '%');

    -- 2. Owners from LINFO02
    SELECT 
        lf02.COMCOD,
        lf02.RSNUM,
        lf02.UNITCOD,
        lf02.KHTNUM,
        lf02.OWNRGRP AS OwnerGroup,
        lf02.OWNRNUM AS OwnerSerial,
        ISNULL(lf02.OWNNAME, '') AS OwnerName,
        ISNULL(lf02.FATHERNAM, '') AS FatherName,
        ISNULL(lf02.MOTHERNAM, '') AS MotherName,
        ISNULL(lf02.SPOUSENAM, '') AS SpouseName,
        ISNULL(lf02.GENDER, '') AS Gender,
        ISNULL(lf02.NIDNUM, '') AS Nid,
        ISNULL(lf02.BREGNUM, '') AS BirthReg,
        ISNULL(lf02.PHONE01, '') AS Phone,
        ISNULL(lf02.EMAIL01, '') AS Email,
        ISNULL(lf02.ADDRESS01, '') AS Address,
        ISNULL(lf02.OWNRAREA, 0) AS OwnerShareArea
    INTO #tblOwner
    FROM dbo_lis.LINFO02 lf02
    INNER JOIN #tblKhatian k ON lf02.COMCOD = k.COMCOD 
                            AND lf02.RSNUM = k.RSNUM 
                            AND lf02.UNITCOD = k.UNITCOD 
                            AND lf02.KHTNUM = k.CurrentKhatian
    WHERE (@OwnerName IS NULL OR lf02.OWNNAME LIKE '%' + @OwnerName + '%');

    -- 3. Plots from LINFO01 with dynamic 3-part Land Classification from LANDUSE
    SELECT 
        lf01.COMCOD,
        lf01.RSNUM,
        lf01.UNITCOD,
        lf01.KHTNUM,
        lf01.PLTNUM AS CurrentPlot,
        ISNULL(lf01.SPLTNUM, '') AS FormerPlot,
        ISNULL(lf01.SKHTNUM, '') AS SabekKhatian,
        ISNULL(lf01.CPLTNUM, '') AS CsPlot,
        lf01.SLNUM AS PlotSerial,
        lf01.LUCODE AS LuCode,
        -- ৪(ক) কৃষি: populated when specific LUTYPE = 'কৃষি'
        CASE WHEN specLu.LUTYPE = N'কৃষি' OR (specLu.LUTYPE LIKE N'%কৃষি%' AND specLu.LUTYPE NOT LIKE N'%অকৃষি%')
             THEN ISNULL(specLu.LUNAME, '') ELSE N'' END AS AgriLandClass_4Ka,
        -- ৪(খ) অকৃষি: populated when specific LUTYPE = 'অকৃষি'
        CASE WHEN specLu.LUTYPE = N'অকৃষি' OR specLu.LUTYPE LIKE N'%অকৃষি%'
             THEN ISNULL(specLu.LUNAME, '') ELSE N'' END AS NonAgriLandClass_4Kha,
        -- ৪(গ) স্থানীয় নাম: parent group (first 2 digits + '000')
        ISNULL(parentLu.LUNAME, '') AS LocalName_4Ga,
        ISNULL(lf01.TPLTAREA, 0) AS TotalPlotArea,
        ISNULL(lf01.KHTSHARE, 0) AS KhatianShare,
        ISNULL(lf01.KPLTAREA, 0) AS KhatianPlotArea,
        ISNULL(lf01.KPLTRMRK, '') AS PlotRemarks
    INTO #tblPlot
    FROM dbo_lis.LINFO01 lf01
    INNER JOIN #tblKhatian k ON lf01.COMCOD = k.COMCOD 
                            AND lf01.RSNUM = k.RSNUM 
                            AND lf01.UNITCOD = k.UNITCOD 
                            AND lf01.KHTNUM = k.CurrentKhatian
    LEFT JOIN dbo.LANDUSE parentLu ON (LEFT(lf01.LUCODE, 2) + '000') = parentLu.LUCODE
    LEFT JOIN dbo.LANDUSE specLu   ON lf01.LUCODE = specLu.LUCODE;

    -- If owner filter is applied, only keep Khatians that matched that owner
    IF @OwnerName IS NOT NULL
    BEGIN
        DELETE k FROM #tblKhatian k WHERE NOT EXISTS (SELECT 1 FROM #tblOwner o WHERE o.KHTNUM = k.CurrentKhatian);
        DELETE p FROM #tblPlot p WHERE NOT EXISTS (SELECT 1 FROM #tblKhatian k WHERE k.CurrentKhatian = p.KHTNUM);
    END

    -- Precompute plot aggregates per khatian
    SELECT 
        KHTNUM,
        COUNT(DISTINCT CurrentPlot) AS TotalPlots,
        SUM(KhatianPlotArea) AS TotalArea
    INTO #tblPlotAgg
    FROM #tblPlot
    GROUP BY KHTNUM;

    -- Precompute concatenated owner names for quick display in Khatian master grid
    SELECT 
        KHTNUM,
        STRING_AGG(OwnerName, N', ') AS AllOwnerNames
    INTO #tblOwnerSummary
    FROM (
        SELECT DISTINCT KHTNUM, OwnerName 
        FROM #tblOwner 
        WHERE LEN(OwnerName) > 0
    ) uo
    GROUP BY KHTNUM;

    -- =========================================================================
    -- Result Set 0: Khatians (with plot count, total land area, all owner names)
    -- =========================================================================
    SELECT 
        ROW_NUMBER() OVER (ORDER BY k.CurrentKhatian) AS SlNo,
        k.COMCOD AS ComCod,
        k.RSNUM AS RsNum,
        k.UNITCOD AS UnitCod,
        k.MouzaName,
        k.SurveyName,
        k.CurrentKhatian,
        k.FormerKhatian,
        k.EntryKhatian,
        k.SheetNum,
        k.LtCode,
        k.LandTypeName,
        k.PorchaTitle,
        ISNULL(agg.TotalPlots, 0) AS TotalPlots,
        ISNULL(agg.TotalArea, 0.0000) AS TotalArea,
        ISNULL(os.AllOwnerNames, N'') AS AllOwnerNames,
        k.KhatianStatus,
        k.KhatianRemarks,
        k.KhatianNote,
        k.USRID AS EntryUserId,
        k.SEALID AS SealId,
        k.SealDesc,
        k.TERMID AS TerminalId,
        k.EntryTime
    FROM #tblKhatian k
    LEFT JOIN #tblPlotAgg agg ON k.CurrentKhatian = agg.KHTNUM
    LEFT JOIN #tblOwnerSummary os ON k.CurrentKhatian = os.KHTNUM
    ORDER BY k.CurrentKhatian;

    -- =========================================================================
    -- Result Set 1: Owners (LINFO02)
    -- =========================================================================
    SELECT 
        ROW_NUMBER() OVER (PARTITION BY o.KHTNUM ORDER BY o.OwnerGroup, o.OwnerSerial) AS SlNo,
        o.COMCOD AS ComCod,
        o.RSNUM AS RsNum,
        o.UNITCOD AS UnitCod,
        o.KHTNUM AS CurrentKhatian,
        o.OwnerGroup,
        o.OwnerSerial,
        o.OwnerName,
        o.FatherName,
        o.MotherName,
        o.SpouseName,
        o.Gender,
        o.Nid,
        o.BirthReg,
        o.Phone,
        o.Email,
        o.Address,
        o.OwnerShareArea
    FROM #tblOwner o
    ORDER BY o.KHTNUM, o.OwnerGroup, o.OwnerSerial;

    -- =========================================================================
    -- Result Set 2: Plots & Land Classification (LINFO01)
    -- =========================================================================
    SELECT 
        ROW_NUMBER() OVER (PARTITION BY p.KHTNUM ORDER BY p.PlotSerial, p.CurrentPlot) AS SlNo,
        p.COMCOD AS ComCod,
        p.RSNUM AS RsNum,
        p.UNITCOD AS UnitCod,
        p.KHTNUM AS CurrentKhatian,
        p.CurrentPlot,
        p.FormerPlot,
        p.SabekKhatian,
        p.CsPlot,
        p.PlotSerial,
        p.LuCode,
        p.AgriLandClass_4Ka,
        p.NonAgriLandClass_4Kha,
        p.LocalName_4Ga,
        p.TotalPlotArea,
        p.KhatianShare,
        p.KhatianPlotArea,
        p.PlotRemarks
    FROM #tblPlot p
    ORDER BY p.KHTNUM, p.PlotSerial, p.CurrentPlot;

    DROP TABLE IF EXISTS #tblKhatian;
    DROP TABLE IF EXISTS #tblOwner;
    DROP TABLE IF EXISTS #tblPlot;
    DROP TABLE IF EXISTS #tblPlotAgg;
    DROP TABLE IF EXISTS #tblOwnerSummary;
END;
GO
