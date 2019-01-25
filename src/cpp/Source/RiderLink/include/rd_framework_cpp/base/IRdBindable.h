//
// Created by jetbrains on 23.07.2018.
//

#ifndef RD_CPP_FRAMEWORK_IRDBINDABLE_H
#define RD_CPP_FRAMEWORK_IRDBINDABLE_H

#include "Lifetime.h"
#include "IRdDynamic.h"
#include "RdId.h"


class IIdentities;

class IRdBindable : public IRdDynamic {
public:
    mutable RdId rdid = RdId::Null();

	mutable IRdDynamic const *parent = nullptr;
    //region ctor/dtor

    IRdBindable() = default;

    IRdBindable(IRdBindable &&other) = default;

    IRdBindable &operator=(IRdBindable &&other) = default;

    virtual ~IRdBindable() = default;
    //endregion

    virtual void bind(Lifetime lf, IRdDynamic const *parent, std::string const &name) const = 0;

    virtual void identify(IIdentities const &identities, RdId const &id) const = 0;
};

template<typename T>
typename std::enable_if<!std::is_base_of<IRdBindable, typename std::remove_reference<T>::type>::value>::type
inline identifyPolymorphic(T &&, IIdentities const &identities, RdId const &id) {}

//template <>
inline void identifyPolymorphic(const IRdBindable &that, IIdentities const &identities, RdId id) {
    that.identify(identities, id);
}

template<typename T>
typename std::enable_if<std::is_base_of<IRdBindable, T>::value>::type
inline identifyPolymorphic(std::vector<T> const &that, IIdentities const &identities, RdId const &id) {
    for (size_t i = 0; i < that.size(); ++i) {
        that[i].identify(identities, id.mix(static_cast<int32_t >(i)));
    }
}

template<typename T>
typename std::enable_if<!std::is_base_of<IRdBindable, typename std::remove_reference<T>::type>::value>::type
inline bindPolymorphic(T &&, Lifetime lf, const IRdDynamic *parent, std::string const &name) {}

inline void bindPolymorphic(IRdBindable const &that, Lifetime lf, const IRdDynamic *parent, std::string const &name) {
    that.bind(lf, parent, name);
}

template<typename T>
typename std::enable_if<std::is_base_of<IRdBindable, T>::value>::type
inline bindPolymorphic(std::vector<T> const &that, Lifetime lf, IRdDynamic const *parent, std::string const &name) {
    for (auto &obj : that) {
        obj.bind(lf, parent, name);
    }
}

#endif //RD_CPP_FRAMEWORK_IRDBINDABLE_H
