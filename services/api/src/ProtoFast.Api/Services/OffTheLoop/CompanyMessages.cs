using ProtoFast.Api.OffTheLoop;
using ProtoFast.Data.OffTheLoop;
using CompanyRecord = ProtoFast.Data.OffTheLoop.Entities.Company;
using ContactRecord = ProtoFast.Data.OffTheLoop.Entities.Contact;

namespace ProtoFast.Api.Services.OffTheLoop;

public static class CompanyMessages
{
    public static Company ToMessage(CompanyRecord company)
    {
        var message = new Company
        {
            Id = company.Id.ToString(),
            Name = company.Name,
            About = company.About,
            Location = company.Location,
            WhatItDoes = company.WhatItDoes,
            HowItMakesMoney = company.HowItMakesMoney,
            Revenue = company.Revenue,
            Growth = company.Growth,
            Headcount = company.Headcount,
            Funding = company.Funding,
            LastRaised = company.LastRaised,
            LastModifiedUnixSeconds = LoopText.UnixSeconds(company.DateLastModified),
        };
        message.Values.AddRange(company.Values);
        message.Practices.AddRange(company.Practices);
        message.Competitors.AddRange(company.Competitors);
        message.Concerns.AddRange(company.Concerns);
        message.Contacts.AddRange(company.Contacts.Select(ToMessage));
        return message;
    }

    public static CompanyRecord ToRecord(Company message)
    {
        var contacts = message.Contacts.Select(ToRecord).ToList();
        if (contacts.Count > OffTheLoopLimits.ListItems)
        {
            throw LoopErrors.Invalid($"A company can list at most {OffTheLoopLimits.ListItems} people.");
        }

        return new CompanyRecord
        {
            Name = LoopText.Required(message.Name, OffTheLoopLimits.Name, "The company name", "Give the company a name."),
            About = LoopText.Optional(message.About, OffTheLoopLimits.Line, "The company line"),
            Location = LoopText.Optional(message.Location, OffTheLoopLimits.Label, "Where the company is based"),
            WhatItDoes = LoopText.Optional(message.WhatItDoes, OffTheLoopLimits.Summary, "What the company does"),
            HowItMakesMoney = LoopText.Optional(message.HowItMakesMoney, OffTheLoopLimits.Summary, "How the company makes money"),
            Revenue = LoopText.Optional(message.Revenue, OffTheLoopLimits.Label, "Revenue"),
            Growth = LoopText.Optional(message.Growth, OffTheLoopLimits.Label, "The growth target"),
            Headcount = LoopText.Optional(message.Headcount, OffTheLoopLimits.Label, "Headcount"),
            Funding = LoopText.Optional(message.Funding, OffTheLoopLimits.Label, "Funding"),
            LastRaised = LoopText.Optional(message.LastRaised, OffTheLoopLimits.Label, "When they last raised"),
            Values = LoopText.Items(message.Values, OffTheLoopLimits.Line, "Values"),
            Practices = LoopText.Items(message.Practices, OffTheLoopLimits.Line, "How it works"),
            Competitors = LoopText.Items(message.Competitors, OffTheLoopLimits.Name, "Competition"),
            Concerns = LoopText.Items(message.Concerns, OffTheLoopLimits.Line, "Concerns"),
            Contacts = contacts,
        };
    }

    public static void Overwrite(CompanyRecord target, CompanyRecord source)
    {
        target.Name = source.Name;
        target.About = source.About;
        target.Location = source.Location;
        target.WhatItDoes = source.WhatItDoes;
        target.HowItMakesMoney = source.HowItMakesMoney;
        target.Revenue = source.Revenue;
        target.Growth = source.Growth;
        target.Headcount = source.Headcount;
        target.Funding = source.Funding;
        target.LastRaised = source.LastRaised;
        target.Values = source.Values;
        target.Practices = source.Practices;
        target.Competitors = source.Competitors;
        target.Concerns = source.Concerns;
        target.Contacts = source.Contacts;
    }

    private static Contact ToMessage(ContactRecord contact)
    {
        var message = new Contact { Name = contact.Name, Role = contact.Role, Note = contact.Note };
        if (contact.MetOn is { } metOn)
        {
            message.MetOn = LoopText.Format(metOn);
        }

        return message;
    }

    private static ContactRecord ToRecord(Contact message) => new()
    {
        Name = LoopText.Required(message.Name, OffTheLoopLimits.Name, "A person’s name", "Give each person a name."),
        Role = LoopText.Optional(message.Role, OffTheLoopLimits.Label, "A person’s role"),
        MetOn = message.HasMetOn ? LoopText.OptionalDay(message.MetOn, "The day you spoke") : null,
        Note = LoopText.Optional(message.Note, OffTheLoopLimits.Line, "A note on a person"),
    };
}
