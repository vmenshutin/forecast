IF EXISTS (SELECT * FROM sys.objects WHERE type = 'P' AND name = 'calculate_forecast')
	DROP PROCEDURE calculate_forecast
GO

create procedure calculate_forecast @demand bit, @web bit, @locationString varchar(20)
as
begin

declare @location INT = CAST(SUBSTRING(@locationString, 0, CHARINDEX(' ', @locationString, 0)) as INT);

if exists (select * from INFORMATION_SCHEMA.TABLES where TABLE_NAME = 'X_FORECAST_TEMP')
    BEGIN
		drop table X_FORECAST_TEMP
    END

--- INCOMING ---

declare @incomingTable table (STOCKCODE varchar(23), INCOMING float, WEB_SHOW char(1), SUPPLIERNO int, DESCRIPTION varchar(100));

insert into @incomingTable
select
stock.STOCKCODE,
lines.INCOMING,
stock.WEB_SHOW,
stock.SUPPLIERNO,
stock.DESCRIPTION
FROM STOCK_ITEMS stock
	left join (
		SELECT
		STOCKCODE,
		SUM(ORD_QUANT - CORRECTION_QUANT - CORRECTED_QUANT + BKORD_QUANT) as INCOMING
		FROM PURCHORD_LINES
		WHERE (ORD_QUANT - CORRECTION_QUANT - CORRECTED_QUANT + BKORD_QUANT) >= 0
		AND LOCATION = @location
		GROUP BY STOCKCODE
	) lines
	on stock.STOCKCODE = lines.STOCKCODE
where stock.STATUS <> 'L'

update @incomingTable
set INCOMING = 0
where INCOMING IS NULL

----------------

--- ON HAND ----

declare @onHandTable table (STOCKCODE varchar(23), ON_HAND float);

insert into @onHandTable
SELECT
stock.STOCKCODE,
info.ON_HAND
from STOCK_ITEMS stock
	left join (
		SELECT
		STOCKCODE,
		QTY AS ON_HAND
		FROM STOCK_LOC_INFO
		WHERE LOCATION = @location
	) info
	on stock.STOCKCODE = info.STOCKCODE
where stock.STATUS <> 'L'

update @onHandTable
set ON_HAND = 0
where ON_HAND IS NULL

----------------

--- OUTGOING ---

declare @outgoingTable table (STOCKCODE varchar(23), OUTGOING float);

insert into @outgoingTable
SELECT
stock.STOCKCODE,
lines.OUTGOING
FROM STOCK_ITEMS stock
left join (
		SELECT
		STOCKCODE,
		SUM (UNSUP_QUANT) AS OUTGOING
		FROM SALESORD_LINES
		WHERE HDR_STATUS IN (0, 1)
		AND UNSUP_QUANT > 0
		AND LOCATION = @location
		GROUP BY STOCKCODE
	) lines
	on stock.STOCKCODE = lines.STOCKCODE
where stock.STATUS <> 'L'

update @outgoingTable
set OUTGOING = 0
where OUTGOING IS NULL

----------------

--- BASE -----

declare @baseTable table (STOCKCODE varchar(23), DEMAND_WEB float, WEB_SHOW char(1), SUPPLIERNO int, DESCRIPTION varchar(100));

insert into @baseTable
SELECT
incoming.STOCKCODE,
(incoming.INCOMING + onHand.ON_HAND - outgoing.OUTGOING) AS DEMAND_WEB,
incoming.WEB_SHOW,
incoming.SUPPLIERNO,
incoming.DESCRIPTION
FROM @incomingTable incoming
LEFT JOIN @onHandTable onHand
	on incoming.STOCKCODE = onHand.STOCKCODE
LEFT JOIN @outgoingTable outgoing
	on incoming.STOCKCODE = outgoing.STOCKCODE
WHERE (incoming.INCOMING + onHand.ON_HAND - outgoing.OUTGOING) < 1

-----------------

if @demand = 1
begin

	--- DEMAND -----

	declare @demandTable table (STOCK varchar(23), DESCRIPTION varchar(100), DEMAND float, SUPPLIERNO int);

	insert into @demandTable
	SELECT
	STOCKCODE as STOCK,
	DESCRIPTION,
	DEMAND_WEB AS DEMAND,
	SUPPLIERNO
	FROM @baseTable base
	WHERE DEMAND_WEB < 0

	-----------------

	if @web = 0
	begin
		select *
		into X_FORECAST_TEMP
		from @demandTable
	end
end

if @web = 1
begin

	--- WEB -----

	declare @webTable table (STOCK varchar(23), DESCRIPTION varchar(100), WEB float, SUPPLIERNO int);

	insert into @webTable
	SELECT
	base.STOCKCODE as STOCK,
	DESCRIPTION,
	DEMAND_WEB AS WEB,
	SUPPLIERNO
	FROM @baseTable base
	WHERE WEB_SHOW = 'Y'

	-----------------

	if @demand = 0
	begin
		select *
		into X_FORECAST_TEMP
		from @webTable
	end

end

if @demand = 1 and @web = 1
begin

	select
		COALESCE(demand.STOCK, web.STOCK) as STOCK,
		COALESCE(demand.DESCRIPTION, web.DESCRIPTION) as DESCRIPTION,
		DEMAND,
		WEB,
		COALESCE(demand.SUPPLIERNO, web.SUPPLIERNO) as SUPPLIERNO
	into X_FORECAST_TEMP
	from @demandTable demand
		FULL JOIN @webTable web
		on demand.STOCK = web.STOCK

end

end

go