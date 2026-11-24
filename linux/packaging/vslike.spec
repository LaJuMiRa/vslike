# Binary RPM for Fedora: repackages linux/dist/vslike-<version>-linux-<arch>.tar.gz (built by linux/build-linux.sh)
# without changes. Build: linux/packaging/build-rpm.sh (runs rpmbuild in a Fedora container or on Fedora).
%global debug_package %{nil}
%global __os_install_post %{nil}
%global __brp_check_rpaths %{nil}

%ifarch x86_64
%global vslike_arch x64
%endif
%ifarch aarch64
%global vslike_arch arm64
%endif

Name:           vslike
Version:        %{vslike_version}
Release:        1%{?dist}
Summary:        C# and .NET IDE with class diagrams, StyleCop and debugger
License:        MIT
URL:            https://github.com/LaJuMiRa/vslike
Source0:        vslike-%{version}-linux-%{vslike_arch}.tar.gz
ExclusiveArch:  x86_64 aarch64
# The bundled .NET assemblies and netcoredbg libraries would produce dependencies on .NET-internal libraries.
AutoReqProv:    no
Requires:       (dotnet-sdk-10.0 or dotnet-sdk-11.0)
Requires:       fontconfig
Requires:       libX11
Requires:       libICE
Requires:       libSM
Requires:       libXcursor
Requires:       libXi
Requires:       libXrandr
Requires:       hicolor-icon-theme
Requires:       shared-mime-info

%description
VsLike is a C# IDE: solution explorer, editor with Roslyn IntelliSense, StyleCop diagnostics and code fixes,
build, run, tests, NuGet, a bidirectional class diagram, terminal and debugger (netcoredbg).

%prep
%setup -q -c -T
tar -xzf %{SOURCE0}

%install
mkdir -p %{buildroot}
cp -a usr %{buildroot}/

%files
%license /usr/share/licenses/vslike/LICENSE
/usr/bin/vslike
/usr/lib/vslike
/usr/share/applications/vslike.desktop
/usr/share/mime/packages/vslike.xml
/usr/share/icons/hicolor/*/apps/vslike.png
