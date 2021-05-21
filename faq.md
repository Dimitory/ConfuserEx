---
layout: page
title: FAQ
group: navigation
permalink: /faq/
---
{% include setup %}

* 
{:toc}

---

What is ConfuserEx?
---

ConfuserEx is an open-source protector for .NET applications. It offers 
advanced security to applications written in C#, VB, F#, and other .NET 
languages.  
ConfuserEx is the successor to [Confuser](http://confuser.codeplex.com) 
project. While Confuser is widely regarded as one of the strongest 
obfuscators available in .NET, ConfuserEx continues to provide excellent 
protections to .NET applications.

{% include scrollToTop.html %}
---


What's special about ConfuserEx?
---

ConfuserEx is special in two ways:

1. ConfuserEx is open-source and free.  
As far as I know, ConfuserEx is the only open-source and free .NET protector 
that has protections comparable with commercial protectors. Since it's 
open-source, you could modify it to suits your need. Consequently, unlike 
other protectors, a generic deobfuscator for different custom version of 
ConfuserEx would be virtually impossible to be created.

2. ConfuserEx has plugin system.    
ConfuserEx's plugin system allows developers to create their own 
protections. This allows different modular protections to be applied in the 
same application, and greatly enhance the security.

{% include scrollToTop.html %}
---


ConfuserEx is open-source, wouldn't it reduce protection strength?
---

Open-source does not necessarily reduce security, and close-source does not 
equals to strong protection.

Take the famous deobfuscator [de4dot](https://github.com/0xd4d/de4dot) as an 
example. As stated in its homepage, it supports many close-sourced commerical 
protectors. It shows that close-sourced protectors are not necessarily secure.

Meanwhile, open-source protectors might not be as insecure as you maight 
thought. The predecessor of ConfuserEx, [Confuser](http://confuser.codeplex.com), 
has been released for 4 years. Only until recently, a public deobfuscator is 
available for it, and it doesn't even have 100% sucess rate in deobfuscation. 
Apparently, Confuser, an open-source protector, provides better security than 
those close-sourced protectors.

{% include scrollToTop.html %}
---


Is ConfuserEx secure?
---

Sure it is!

ConfuserEx has many techniques that commerical protector used, and some more! 
If you have any doubt about protection strength, feel free to contact me!

{% include scrollToTop.html %}
---


Is there any GUI for ConfuserEX?
---

A GUI is available since version 0.2.0. Go check it out!

{% include scrollToTop.html %}
---


What about documentation?
---

Documentation is now available at [the project wiki](https://github.com/mkaring/ConfuserEx/wiki)!
If you still got any problem, please don’t hesitate to contact me or open an issue!

{% include scrollToTop.html %}
---


How to migrate from Confuser to ConfuserEx?
---

ConfuserEx's project format is quite similar to Confuser's. There are few places 
to note:

- `assembly` and `confusion` elements are replaced by `module` and `protection`
elements respectively.
- You should supply `basePath` attribute and use relative path in other places 
of project file.
- the rules are now a expression that matching the items, see the documentation 
for details.

{% include scrollToTop.html %}
---