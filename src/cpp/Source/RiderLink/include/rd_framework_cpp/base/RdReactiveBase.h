//
// Created by jetbrains on 23.07.2018.
//

#ifndef RD_CPP_RDREACTIVEBASE_H
#define RD_CPP_RDREACTIVEBASE_H


#include "RdBindableBase.h"
#include "IRdReactive.h"
#include "IWire.h"
#include "IProtocol.h"
#include "Logger.h"

#pragma warning( push )
#pragma warning( disable:4250 )

class RdReactiveBase : public RdBindableBase, public IRdReactive {
    class local_change_handler {
        RdReactiveBase const *ptr;
    public:
        local_change_handler(RdReactiveBase const *ptr) : ptr(ptr) {
            ptr->is_local_change = true;
        }

        ~local_change_handler() {
            ptr->is_local_change = false;
        }
    };

public:
    static rd::Logger logReceived;
	static rd::Logger logSend;

    //region ctor/dtor

    RdReactiveBase() = default;

    RdReactiveBase(RdReactiveBase &&other);

    RdReactiveBase &operator=(RdReactiveBase &&other);

    virtual ~RdReactiveBase() = default;
    //endregion

    const IWire *const get_wire() const;

    mutable bool is_local_change = false;

    //delegated
    const Serializers &get_serializers() {
        return get_protocol()->serializers;
    }

    const Serializers &get_serializers() const;

    IScheduler *get_default_scheduler() const;

    IScheduler *get_wire_scheduler() const override;

    void assert_threading() const;

    void assert_bound() const;

    template<typename F>
    auto local_change(F &&action) const -> typename std::result_of<F()>::type {
        if (is_bound() && !async) {
            assert_threading();
        }

        MY_ASSERT_MSG(!is_local_change, "!isLocalChange for RdReactiveBase with id:" + rdid.toString());

        local_change_handler lc_handler(this);
        return action();
    }
};

#pragma warning( pop )

#endif //RD_CPP_RDREACTIVEBASE_H
