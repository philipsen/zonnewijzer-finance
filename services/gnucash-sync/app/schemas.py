from pydantic import BaseModel, ConfigDict, Field


class InvoiceLineIn(BaseModel):
    model_config = ConfigDict(populate_by_name=True)

    description: str = ""
    quantity: float = 0
    unit_price: float = Field(0, alias="unitPrice")
    line_amount: float = Field(0, alias="lineAmount")


class InvoiceIn(BaseModel):
    model_config = ConfigDict(populate_by_name=True)

    invoice_id: str = Field(alias="invoiceId")
    customer_id: str = Field(alias="customerId")
    customer_name: str = Field(alias="customerName")
    street: str | None = None
    house_number: str | None = Field(None, alias="houseNumber")
    postal_code: str | None = Field(None, alias="postalCode")
    place_name: str | None = Field(None, alias="placeName")
    email: str | None = None
    year: int = 0
    month: int = 0
    invoice_date_opened: str = Field(alias="invoiceDateOpened")
    currency: str = "EUR"
    mollie_paid: bool = Field(False, alias="molliePaid")
    lines: list[InvoiceLineIn] = Field(default_factory=list)


class InsertBody(BaseModel):
    model_config = ConfigDict(populate_by_name=True)

    schema_version: int = Field(1, alias="schemaVersion")
    invoices: list[InvoiceIn] = Field(default_factory=list)


class ResultItem(BaseModel):
    invoiceId: str
    status: str
    detail: str | None = None


class InsertResponse(BaseModel):
    results: list[ResultItem]
