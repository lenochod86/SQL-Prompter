-- A small shop database for trying out suggestions with foreign keys and lazy loading of metadata.
-- Run it on any test server (the login needs dbcreator): ./.claude/skills/test-database/invoke-sql.ps1 -File tests/TestDatabase/shop.sql
-- It drops and recreates the database SqlPrompterTest_Shop. Three schemas, 12 tables, several foreign keys (also a self reference,
-- a composite key and tables reached through two paths), about ten rows in every table, a view and a procedure.
IF DB_ID(N'SqlPrompterTest_Shop') IS NOT NULL
BEGIN
    ALTER DATABASE SqlPrompterTest_Shop SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE SqlPrompterTest_Shop;
END
GO
CREATE DATABASE SqlPrompterTest_Shop;
GO
USE SqlPrompterTest_Shop;
GO
CREATE SCHEMA hr;
GO
CREATE SCHEMA sales;
GO
CREATE TABLE dbo.Countries (
    IdCountry int IDENTITY(1, 1) CONSTRAINT PK_Countries PRIMARY KEY,
    Code char(2) NOT NULL,
    Name nvarchar(60) NOT NULL
);
CREATE TABLE dbo.Cities (
    IdCity int IDENTITY(1, 1) CONSTRAINT PK_Cities PRIMARY KEY,
    IdCountry int NOT NULL CONSTRAINT FK_Cities_Countries FOREIGN KEY REFERENCES dbo.Countries (IdCountry),
    Name nvarchar(80) NOT NULL,
    Population int NULL
);
CREATE TABLE dbo.AuditLog (
    IdLog bigint IDENTITY(1, 1) CONSTRAINT PK_AuditLog PRIMARY KEY,
    TableName sysname NOT NULL,
    Action varchar(10) NOT NULL,
    LoggedAt datetime2 NOT NULL CONSTRAINT DF_AuditLog_LoggedAt DEFAULT SYSDATETIME()
);
CREATE TABLE hr.Departments (
    IdDepartment int IDENTITY(1, 1) CONSTRAINT PK_Departments PRIMARY KEY,
    Name nvarchar(60) NOT NULL
);
CREATE TABLE hr.Employees (
    IdEmployee int IDENTITY(1, 1) CONSTRAINT PK_Employees PRIMARY KEY,
    IdDepartment int NOT NULL CONSTRAINT FK_Employees_Departments FOREIGN KEY REFERENCES hr.Departments (IdDepartment),
    IdManager int NULL CONSTRAINT FK_Employees_Manager FOREIGN KEY REFERENCES hr.Employees (IdEmployee),
    IdCity int NULL CONSTRAINT FK_Employees_Cities FOREIGN KEY REFERENCES dbo.Cities (IdCity),
    FirstName nvarchar(40) NOT NULL,
    LastName nvarchar(40) NOT NULL,
    HireDate date NOT NULL,
    Salary decimal(10, 2) NOT NULL
);
CREATE TABLE sales.Customers (
    IdCustomer int IDENTITY(1, 1) CONSTRAINT PK_Customers PRIMARY KEY,
    IdCity int NULL CONSTRAINT FK_Customers_Cities FOREIGN KEY REFERENCES dbo.Cities (IdCity),
    Name nvarchar(80) NOT NULL,
    Email varchar(120) NULL,
    CreatedAt datetime2 NOT NULL CONSTRAINT DF_Customers_CreatedAt DEFAULT SYSDATETIME()
);
CREATE TABLE sales.ProductCategories (
    IdCategory int IDENTITY(1, 1) CONSTRAINT PK_ProductCategories PRIMARY KEY,
    IdParent int NULL CONSTRAINT FK_ProductCategories_Parent FOREIGN KEY REFERENCES sales.ProductCategories (IdCategory),
    Name nvarchar(60) NOT NULL
);
CREATE TABLE sales.Products (
    IdProduct int IDENTITY(1, 1) CONSTRAINT PK_Products PRIMARY KEY,
    IdCategory int NOT NULL CONSTRAINT FK_Products_ProductCategories FOREIGN KEY REFERENCES sales.ProductCategories (IdCategory),
    Name nvarchar(80) NOT NULL,
    Price decimal(10, 2) NOT NULL,
    Discontinued bit NOT NULL CONSTRAINT DF_Products_Discontinued DEFAULT 0
);
CREATE TABLE sales.Orders (
    IdOrder int IDENTITY(1, 1) CONSTRAINT PK_Orders PRIMARY KEY,
    IdCustomer int NOT NULL CONSTRAINT FK_Orders_Customers FOREIGN KEY REFERENCES sales.Customers (IdCustomer),
    IdEmployee int NULL CONSTRAINT FK_Orders_Employees FOREIGN KEY REFERENCES hr.Employees (IdEmployee),
    OrderDate date NOT NULL,
    Status varchar(12) NOT NULL
);
CREATE TABLE sales.OrderItems (
    IdOrderItem int IDENTITY(1, 1) CONSTRAINT PK_OrderItems PRIMARY KEY,
    IdOrder int NOT NULL CONSTRAINT FK_OrderItems_Orders FOREIGN KEY REFERENCES sales.Orders (IdOrder),
    LineNumber int NOT NULL,
    IdProduct int NOT NULL CONSTRAINT FK_OrderItems_Products FOREIGN KEY REFERENCES sales.Products (IdProduct),
    Quantity int NOT NULL,
    UnitPrice decimal(10, 2) NOT NULL,
    CONSTRAINT UQ_OrderItems_Line UNIQUE (IdOrder, LineNumber)
);
CREATE TABLE sales.ShipmentLines (
    IdShipmentLine int IDENTITY(1, 1) CONSTRAINT PK_ShipmentLines PRIMARY KEY,
    IdOrder int NOT NULL,
    LineNumber int NOT NULL,
    Quantity int NOT NULL,
    ShippedAt datetime2 NOT NULL,
    CONSTRAINT FK_ShipmentLines_OrderItems FOREIGN KEY (IdOrder, LineNumber) REFERENCES sales.OrderItems (IdOrder, LineNumber)
);
CREATE TABLE sales.Payments (
    IdPayment int IDENTITY(1, 1) CONSTRAINT PK_Payments PRIMARY KEY,
    IdOrder int NOT NULL CONSTRAINT FK_Payments_Orders FOREIGN KEY REFERENCES sales.Orders (IdOrder),
    Amount decimal(10, 2) NOT NULL,
    Method varchar(10) NOT NULL,
    PaidAt datetime2 NOT NULL
);
GO
INSERT dbo.Countries (Code, Name) VALUES
    ('CZ', N'Czechia'), ('SK', N'Slovakia'), ('DE', N'Germany'), ('AT', N'Austria'), ('PL', N'Poland'),
    ('HU', N'Hungary'), ('FR', N'France'), ('IT', N'Italy'), ('ES', N'Spain'), ('NL', N'Netherlands');
INSERT dbo.Cities (IdCountry, Name, Population) VALUES
    (1, N'Prague', 1300000), (1, N'Brno', 380000), (2, N'Bratislava', 440000), (3, N'Berlin', 3600000), (3, N'Munich', 1500000),
    (4, N'Vienna', 1900000), (5, N'Warsaw', 1800000), (6, N'Budapest', 1700000), (7, N'Lyon', 520000), (10, N'Utrecht', 360000);
INSERT dbo.AuditLog (TableName, Action) VALUES
    ('Orders', 'INSERT'), ('Orders', 'UPDATE'), ('Customers', 'INSERT'), ('Products', 'UPDATE'), ('Payments', 'INSERT'),
    ('Orders', 'UPDATE'), ('Customers', 'UPDATE'), ('Products', 'INSERT'), ('Payments', 'INSERT'), ('Orders', 'DELETE');
INSERT hr.Departments (Name) VALUES
    (N'Management'), (N'Sales'), (N'Warehouse'), (N'Support'), (N'Finance'), (N'Marketing'), (N'IT'), (N'Purchasing'), (N'Legal'), (N'Quality');
INSERT hr.Employees (IdDepartment, IdManager, IdCity, FirstName, LastName, HireDate, Salary) VALUES
    (1, NULL, 1, N'Anna', N'Novak', '2015-03-01', 98000), (2, 1, 1, N'Petr', N'Svoboda', '2017-06-15', 61000),
    (2, 2, 2, N'Jana', N'Dvorak', '2019-01-07', 54000), (3, 1, 3, N'Tomas', N'Cerny', '2018-09-03', 47000),
    (4, 1, 4, N'Eva', N'Prochazka', '2020-02-17', 45000), (5, 1, 1, N'Karel', N'Kucera', '2016-11-21', 72000),
    (6, 1, 5, N'Lucie', N'Vesely', '2021-05-10', 52000), (7, 1, 1, N'Martin', N'Horak', '2014-08-04', 83000),
    (2, 2, 6, N'Hana', N'Nemec', '2022-03-14', 50000), (3, 4, 2, N'Ondrej', N'Pokorny', '2023-01-09', 43000);
INSERT sales.Customers (IdCity, Name, Email) VALUES
    (1, N'Alfa Trading', 'info@alfa.example'), (2, N'Beta Stores', 'orders@beta.example'), (3, N'Gamma Retail', NULL),
    (4, N'Delta Foods', 'buy@delta.example'), (5, N'Epsilon Tools', 'hello@epsilon.example'), (6, N'Zeta Garden', NULL),
    (7, N'Eta Books', 'shop@eta.example'), (8, N'Theta Sports', 'sales@theta.example'), (9, N'Iota Toys', NULL), (10, N'Kappa Cafe', 'cafe@kappa.example');
INSERT sales.ProductCategories (IdParent, Name) VALUES
    (NULL, N'Food'), (NULL, N'Tools'), (NULL, N'Books'), (1, N'Snacks'), (1, N'Drinks'),
    (2, N'Hand tools'), (2, N'Power tools'), (3, N'Fiction'), (3, N'Technical'), (NULL, N'Toys');
INSERT sales.Products (IdCategory, Name, Price, Discontinued) VALUES
    (4, N'Salted peanuts', 2.50, 0), (5, N'Sparkling water 1l', 0.90, 0), (6, N'Hammer 500 g', 12.00, 0), (7, N'Cordless drill', 89.90, 0),
    (8, N'Crime novel', 9.99, 0), (9, N'SQL handbook', 34.50, 0), (10, N'Wooden train', 24.00, 1), (4, N'Chocolate bar', 1.40, 0),
    (6, N'Screwdriver set', 15.50, 0), (9, N'Index tuning guide', 42.00, 0);
INSERT sales.Orders (IdCustomer, IdEmployee, OrderDate, Status) VALUES
    (1, 2, '2026-01-05', 'Shipped'), (2, 3, '2026-01-12', 'Shipped'), (3, 2, '2026-02-02', 'Paid'), (4, 9, '2026-02-14', 'Shipped'),
    (5, 3, '2026-03-01', 'New'), (6, 2, '2026-03-09', 'Paid'), (7, 9, '2026-03-22', 'Cancelled'), (8, 3, '2026-04-04', 'Shipped'),
    (9, 2, '2026-04-18', 'New'), (10, 9, '2026-05-02', 'Paid');
INSERT sales.OrderItems (IdOrder, LineNumber, IdProduct, Quantity, UnitPrice) VALUES
    (1, 1, 1, 10, 2.50), (2, 1, 3, 2, 12.00), (3, 1, 4, 1, 89.90), (4, 1, 8, 24, 1.40), (5, 1, 5, 3, 9.99),
    (6, 1, 6, 1, 34.50), (7, 1, 7, 1, 24.00), (8, 1, 9, 2, 15.50), (9, 1, 10, 1, 42.00), (10, 1, 2, 12, 0.90);
INSERT sales.ShipmentLines (IdOrder, LineNumber, Quantity, ShippedAt) VALUES
    (1, 1, 10, '2026-01-06'), (2, 1, 2, '2026-01-13'), (3, 1, 1, '2026-02-03'), (4, 1, 24, '2026-02-15'), (5, 1, 3, '2026-03-02'),
    (6, 1, 1, '2026-03-10'), (7, 1, 1, '2026-03-23'), (8, 1, 2, '2026-04-05'), (9, 1, 1, '2026-04-19'), (10, 1, 12, '2026-05-03');
INSERT sales.Payments (IdOrder, Amount, Method, PaidAt) VALUES
    (1, 25.00, 'Card', '2026-01-05'), (2, 24.00, 'Transfer', '2026-01-13'), (3, 89.90, 'Card', '2026-02-02'), (4, 33.60, 'Cash', '2026-02-15'),
    (6, 34.50, 'Card', '2026-03-09'), (8, 31.00, 'Transfer', '2026-04-05'), (10, 10.80, 'Card', '2026-05-02'), (1, 5.00, 'Cash', '2026-01-06'),
    (3, 10.00, 'Cash', '2026-02-03'), (9, 42.00, 'Transfer', '2026-04-19');
GO
CREATE VIEW sales.vOrderTotals AS
SELECT o.IdOrder, c.Name AS Customer, o.OrderDate, o.Status, SUM(i.Quantity * i.UnitPrice) AS Total
FROM sales.Orders o
INNER JOIN sales.Customers c ON c.IdCustomer = o.IdCustomer
INNER JOIN sales.OrderItems i ON i.IdOrder = o.IdOrder
GROUP BY o.IdOrder, c.Name, o.OrderDate, o.Status;
GO
CREATE PROCEDURE sales.GetOrdersOfCustomer @IdCustomer int, @From date = NULL
AS
SELECT o.IdOrder, o.OrderDate, o.Status
FROM sales.Orders o
WHERE o.IdCustomer = @IdCustomer AND (@From IS NULL OR o.OrderDate >= @From);
GO
