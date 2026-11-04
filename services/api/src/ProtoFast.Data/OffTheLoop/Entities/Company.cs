using ProtoFast.Database.Abstractions;

namespace ProtoFast.Data.OffTheLoop.Entities;

public sealed class Company : IDateStamped
{
    public Guid Id { get; set; }

    public string UserId { get; set; } = "";

    public required string Name { get; set; }

    /// <summary>One line on it: "fintech · payments api · remote (us)".</summary>
    public string About { get; set; } = "";

    /// <summary>Where it's based: "Burbank, CA".</summary>
    public string Location { get; set; } = "";

    public string WhatItDoes { get; set; } = "";

    public string HowItMakesMoney { get; set; } = "";

    public string Revenue { get; set; } = "";

    public string Growth { get; set; } = "";

    public string Headcount { get; set; } = "";

    public string Funding { get; set; } = "";

    /// <summary>When the last round closed: "Mar 2025".</summary>
    public string LastRaised { get; set; } = "";

    public List<string> Values { get; set; } = [];

    /// <summary>How it works: on-call, remote policy, process.</summary>
    public List<string> Practices { get; set; } = [];

    public List<string> Competitors { get; set; } = [];

    public List<string> Concerns { get; set; } = [];

    public List<Contact> Contacts { get; set; } = [];

    public DateTime DateCreated { get; set; }

    public DateTime DateLastModified { get; set; }
}
